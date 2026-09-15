using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application.Contracts.Modules.Clients;
using SecureClientPortal.Backend.Application.Identity;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Infrastructure.Modules.Clients;
using SecureClientPortal.Backend.Models;

namespace SecureClientPortal.Tests;

public sealed class ClientOnboardingTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly Guid AccountantId = Guid.NewGuid();
    private static ClaimsPrincipal Actor(string role = "admin", Guid? id = null) => new(new ClaimsIdentity([
        new Claim(ClaimTypes.NameIdentifier, (id ?? Guid.NewGuid()).ToString()), new Claim(ClaimTypes.Role, role)
    ], "test"));
    private static User Person(Guid id, string email, UserRole role, Guid[]? clients = null)
    {
        var person = User.CreateInvited(id, "Test person", email, role, "hash", JsonSerializer.Serialize(clients ?? []), null);
        person.CompleteSetup("Test person", "hash");
        return person;
    }
    private static PortalDbContext Database()
    {
        var db = new PortalDbContext(new DbContextOptionsBuilder<PortalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.RoleDefinitions.AddRange(RoleDefinition.Create("client", "Client", "client", "[]", true, true),
            RoleDefinition.Create("accountant", "Accountant", "accountant", "[]", true, true));
        db.Users.Add(Person(AccountantId, "accountant@example.test", UserRole.Accountant));
        db.SaveChanges();
        return db;
    }
    private static OnboardClientRequest Request() => new(Guid.NewGuid(), "New business", "Pty Ltd", "Construction",
        "Client contact", "client@example.test", AccountantId, null, "2026/12345");
    private static ClientOnboardingService Service(PortalDbContext db, Messaging? messages = null) => new(db, messages ?? new(), new Links());

    [Fact]
    public async Task CreatesBusinessLinkedUserAndAssignmentTogether_WithoutExposingSecrets()
    {
        await using var db = Database();
        var mail = new Messaging();
        var response = await Service(db, mail).CreateAsync(Request(), Actor(), Ct);
        Assert.Null(response.Error);
        var result = response.Value!;
        Assert.Equal("smtp", result.InvitationDelivery);
        var client = await db.Clients.SingleAsync(Ct);
        var user = await db.Users.SingleAsync(x => x.Role == "client", Ct);
        Assert.Contains(client.Id, JsonSerializer.Deserialize<Guid[]>(user.ClientIdsJson)!);
        Assert.Equal(AccountantId, client.AssignedAccountantId);
        Assert.Equal("Construction", client.Industry);
        Assert.Equal("Pty Ltd", client.EntityType);
        Assert.Equal(client.Id, (await db.ClientAssignments.SingleAsync(Ct)).ClientId);
        Assert.Equal(user.Id, (await db.UserAccessTokens.SingleAsync(Ct)).UserId);
        Assert.Single(mail.Recipients);
        Assert.Contains(await db.AuditLogs.ToListAsync(Ct), a => a.Action == "clients.onboarded");
        var json = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        var scopedClient = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Role, "client")], "test"));
        Assert.Contains(client.Id, await scopedClient.GetAccessibleClientIdsAsync(db, Ct));
        Assert.Contains(client.Id, await Actor("accountant", AccountantId).GetAccessibleClientIdsAsync(db, Ct));
    }

    [Fact]
    public async Task LinksAnExistingClientUser_PreservingEarlierBusinesses_WithoutAnotherInvite()
    {
        await using var db = Database();
        var previousId = Guid.NewGuid();
        var existing = Person(Guid.NewGuid(), "existing@example.test", UserRole.Client, [previousId]);
        db.Users.Add(existing); await db.SaveChangesAsync(Ct);
        var mail = new Messaging();
        var response = await Service(db, mail).CreateAsync(Request() with { ExistingClientUserId = existing.Id }, Actor(), Ct);
        Assert.Null(response.Error);
        Assert.False(response.Value!.UserCreated);
        Assert.Equal("not_required", response.Value.InvitationDelivery);
        var ids = JsonSerializer.Deserialize<Guid[]>(existing.ClientIdsJson)!;
        Assert.Contains(previousId, ids);
        Assert.Contains(response.Value.ClientId, ids);
        Assert.Empty(mail.Recipients);
        Assert.Empty(await db.UserAccessTokens.ToListAsync(Ct));
    }

    [Fact]
    public async Task RetryReturnsOriginalResult_AndChangedPayloadCannotReuseKey()
    {
        await using var db = Database();
        var mail = new Messaging(); var service = Service(db, mail); var request = Request();
        var first = await service.CreateAsync(request, Actor(), Ct);
        var retry = await service.CreateAsync(request, Actor(), Ct);
        Assert.Equal(first.Value!.ClientId, retry.Value!.ClientId);
        Assert.Single(await db.Clients.ToListAsync(Ct));
        Assert.Single(mail.Recipients);
        Assert.Equal(409, (await service.CreateAsync(request with { Name = "Different business" }, Actor(), Ct)).StatusCode);
    }

    [Fact]
    public async Task EmailFailureIsReportedWithoutDuplicatingOrLosingTheBusiness()
    {
        await using var db = Database();
        var mail = new Messaging { Fail = true }; var service = Service(db, mail); var request = Request();
        var first = await service.CreateAsync(request, Actor(), Ct);
        Assert.Null(first.Error);
        Assert.Equal("failed", first.Value!.InvitationDelivery);
        Assert.Contains("saved", first.Value.Message);
        Assert.Equal(first.Value.ClientId, (await service.CreateAsync(request, Actor(), Ct)).Value!.ClientId);
        Assert.Single(await db.Clients.ToListAsync(Ct));
        Assert.Single(await db.ClientAssignments.ToListAsync(Ct));
    }

    [Theory]
    [InlineData("client")]
    [InlineData("accountant")]
    public async Task NonAdminsCannotOnboardOrListLinkTargets(string role)
    {
        await using var db = Database();
        Assert.True((await Service(db).CreateAsync(Request(), Actor(role), Ct)).Forbidden);
        Assert.True((await Service(db).GetOptionsAsync(Actor(role), Ct)).Forbidden);
        Assert.Empty(await db.Clients.ToListAsync(Ct));
    }

    [Fact]
    public async Task InvalidAccountantOrStaffLinkDoesNotPartiallyCreateAnything()
    {
        await using var db = Database(); var service = Service(db);
        Assert.NotNull((await service.CreateAsync(Request() with { AccountantUserId = Guid.NewGuid() }, Actor(), Ct)).Error);
        Assert.NotNull((await service.CreateAsync(Request() with { ExistingClientUserId = AccountantId }, Actor(), Ct)).Error);
        Assert.Empty(await db.Clients.ToListAsync(Ct));
        Assert.Empty(await db.ClientAssignments.ToListAsync(Ct));
        Assert.Empty(await db.UserAccessTokens.ToListAsync(Ct));
    }

    [Fact]
    public async Task ExistingEmailRequiresExplicitLink_AndDuplicateRegistrationIsRejected()
    {
        await using var db = Database(); var service = Service(db);
        db.Users.Add(Person(Guid.NewGuid(), "client@example.test", UserRole.Client));
        await db.SaveChangesAsync(Ct);
        Assert.Equal(409, (await service.CreateAsync(Request(), Actor(), Ct)).StatusCode);
        Assert.Empty(await db.Clients.ToListAsync(Ct));
        Assert.Null((await service.CreateAsync(Request() with { ContactEmail = "new@example.test" }, Actor(), Ct)).Error);
        Assert.Equal(409, (await service.CreateAsync(Request() with { ContactEmail = "third@example.test" }, Actor(), Ct)).StatusCode);
        Assert.Single(await db.Clients.ToListAsync(Ct));
    }

    private sealed class Messaging : IAccessEmailSender
    {
        public bool Fail { get; init; }
        public List<string> Recipients { get; } = [];
        public Task<AccessEmailDispatchResult> SendInviteAsync(string email, string name, string url, DateTime expiry, CancellationToken ct)
        {
            if (Fail) throw new IOException("SMTP unavailable");
            Recipients.Add(email);
            return Task.FromResult(new AccessEmailDispatchResult("smtp"));
        }
        public Task<AccessEmailDispatchResult> SendPasswordResetAsync(string email, string name, string url, DateTime expiry, CancellationToken ct) =>
            throw new NotSupportedException();
    }
    private sealed class Links : IAccessLinkBuilder
    {
        public string BuildSetupUrl(string email, string token) => "https://portal.example.test/setup?token=" + token;
        public string BuildPasswordResetUrl(string email, string token) => throw new NotSupportedException();
    }
}
