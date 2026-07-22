using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Infrastructure.Modules.Requests;
using SecureClientPortal.Backend.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;

namespace SecureClientPortal.Backend.Tests;

public class Phase3RequestsCommentsTests
{
    private static readonly Guid AccountantUserId = Guid.Parse("82222222-2222-2222-2222-222222222221");
    private static readonly Guid AccountantTwoId = Guid.Parse("82222222-2222-2222-2222-222222222222");
    private static readonly Guid ClientUserId = Guid.Parse("83333333-3333-3333-3333-333333333331");
    private static readonly Guid ClientAlphaId = Guid.Parse("8aaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1");
    private static readonly Guid ClientBetaId = Guid.Parse("8aaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2");
    private static readonly Guid RequestAlphaId = Guid.Parse("85555555-5555-5555-5555-555555555551");
    private static readonly Guid RequestBetaId = Guid.Parse("85555555-5555-5555-5555-555555555552");

    [Fact]
    public async Task GetComments_RespectsScope()
    {
        await using var db = BuildDb();
        Seed(db);

        var accountant = BuildUser(AccountantUserId, "accountant");
        var okController = new RequestsController(RequestService.CreateForTests(db)) { ControllerContext = BuildControllerContext(accountant) };
        var okResult = await okController.GetComments(RequestAlphaId.ToString(), TestContext.Current.CancellationToken);
        var ok = Assert.IsType<OkObjectResult>(okResult.Result);
        var comments = Assert.IsAssignableFrom<IEnumerable<RequestComment>>(ok.Value);
        Assert.Single(comments);

        var blockedController = new RequestsController(RequestService.CreateForTests(db)) { ControllerContext = BuildControllerContext(accountant) };
        var blocked = await blockedController.GetComments(RequestBetaId.ToString(), TestContext.Current.CancellationToken);
        Assert.IsType<ForbidResult>(blocked.Result);
    }

    [Fact]
    public async Task AddComment_FromClient_CreatesAccountantNotification()
    {
        await using var db = BuildDb();
        Seed(db);

        var client = BuildUser(ClientUserId, "client", [ClientAlphaId]);
        var controller = new RequestsController(RequestService.CreateForTests(db))
        {
            ControllerContext = BuildControllerContext(client)
        };

        var result = await controller.AddComment(
            RequestAlphaId.ToString(),
            new AddRequestCommentRequest("Need an update"),
            TestContext.Current.CancellationToken);
        Assert.IsType<OkObjectResult>(result);

        var notifications = await db.Notifications.Where(x => x.ClientId == ClientAlphaId).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Contains(notifications, x => x.UserId == AccountantUserId && x.Type == "request.comment");
    }

    [Fact]
    public async Task AddComment_InternalNoteFromClient_IsRejected()
    {
        await using var db = BuildDb();
        Seed(db);

        var client = BuildUser(ClientUserId, "client", [ClientAlphaId]);
        var controller = new RequestsController(RequestService.CreateForTests(db))
        {
            ControllerContext = BuildControllerContext(client)
        };

        var result = await controller.AddComment(
            RequestAlphaId.ToString(),
            new AddRequestCommentRequest("Internal note", true),
            TestContext.Current.CancellationToken);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var payload = JsonSerializer.Serialize(badRequest.Value);
        Assert.Contains("Clients cannot add internal notes.", payload);
    }

    [Fact]
    public async Task GetComments_HidesInternalNotesFromClient()
    {
        await using var db = BuildDb();
        Seed(db);

        db.RequestComments.Add(RequestComment.Create(
            Guid.Parse("89999999-9999-9999-9999-999999999992"),
            RequestAlphaId,
            ClientAlphaId,
            AccountantUserId,
            "accountant",
            true,
            "Internal escalation note"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var client = BuildUser(ClientUserId, "client", [ClientAlphaId]);
        var controller = new RequestsController(RequestService.CreateForTests(db))
        {
            ControllerContext = BuildControllerContext(client)
        };

        var result = await controller.GetComments(RequestAlphaId.ToString(), TestContext.Current.CancellationToken);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var comments = Assert.IsAssignableFrom<IEnumerable<RequestComment>>(ok.Value).ToList();

        Assert.Single(comments);
        Assert.DoesNotContain(comments, x => x.IsInternal);
    }

    [Fact]
    public async Task Escalate_CreatesInternalCommentAndAdminNotification()
    {
        await using var db = BuildDb();
        Seed(db);

        var adminUserId = Guid.Parse("81111111-1111-1111-1111-111111111111");
        db.Users.Add(BuildActiveUser(adminUserId, "Admin", "admin@test.com", UserRole.Admin));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var accountant = BuildUser(AccountantUserId, "accountant");
        var controller = new RequestsController(RequestService.CreateForTests(db))
        {
            ControllerContext = BuildControllerContext(accountant)
        };

        var result = await controller.Escalate(
            RequestAlphaId.ToString(),
            new EscalateRequestRequest("Need admin review", "admin"),
            TestContext.Current.CancellationToken);

        Assert.IsType<OkObjectResult>(result);

        var comment = await db.RequestComments
            .Where(x => x.RequestId == RequestAlphaId && x.IsInternal)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(comment);
        Assert.Contains("Need admin review", comment.Message);

        var notification = await db.Notifications
            .FirstOrDefaultAsync(
                x => x.ClientId == ClientAlphaId &&
                     x.UserId == adminUserId &&
                     x.Type == "request.escalation",
                TestContext.Current.CancellationToken);

        Assert.NotNull(notification);
    }

    [Fact]
    public async Task AddComment_InternalNoteFromAccountant_DoesNotNotifyClient_AndDoesNotChangeStatus()
    {
        await using var db = BuildDb();
        Seed(db);

        var request = await db.Requests.FirstAsync(x => x.Id == RequestAlphaId, TestContext.Current.CancellationToken);
        request.MarkWaitingOnClient();
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var accountant = BuildUser(AccountantUserId, "accountant");
        var controller = new RequestsController(RequestService.CreateForTests(db))
        {
            ControllerContext = BuildControllerContext(accountant)
        };

        var result = await controller.AddComment(
            RequestAlphaId.ToString(),
            new AddRequestCommentRequest("Internal workflow note", true),
            TestContext.Current.CancellationToken);

        Assert.IsType<OkObjectResult>(result);

        var updated = await db.Requests.FirstAsync(x => x.Id == RequestAlphaId, TestContext.Current.CancellationToken);
        Assert.Equal("waiting_on_client", updated.Status);

        var comment = await db.RequestComments
            .Where(x => x.RequestId == RequestAlphaId && x.IsInternal)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(comment);

        var notifications = await db.Notifications
            .Where(x => x.ClientId == ClientAlphaId)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(notifications, x => x.UserId == ClientUserId && x.Type == "request.comment");
    }

    [Fact]
    public async Task GetWorkspace_HidesInternalNotesFromClient()
    {
        await using var db = BuildDb();
        Seed(db);

        db.RequestComments.Add(RequestComment.Create(
            Guid.Parse("89999999-9999-9999-9999-999999999993"),
            RequestAlphaId,
            ClientAlphaId,
            AccountantUserId,
            "accountant",
            true,
            "Internal workspace note"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var client = BuildUser(ClientUserId, "client", [ClientAlphaId]);
        var controller = new RequestsController(RequestService.CreateForTests(db))
        {
            ControllerContext = BuildControllerContext(client)
        };

        var result = await controller.GetWorkspace(RequestAlphaId.ToString(), TestContext.Current.CancellationToken);
        var ok = Assert.IsType<OkObjectResult>(result);
        var workspace = Assert.IsType<RequestWorkspaceResponse>(ok.Value);

        Assert.Single(workspace.Comments);
        Assert.DoesNotContain(workspace.Comments, x => x.IsInternal);
    }

    [Fact]
    public async Task UpdateStatus_FromClient_IsRejected()
    {
        await using var db = BuildDb();
        Seed(db);

        var client = BuildUser(ClientUserId, "client", [ClientAlphaId]);
        var controller = new RequestsController(RequestService.CreateForTests(db))
        {
            ControllerContext = BuildControllerContext(client)
        };

        var result = await controller.UpdateStatus(
            RequestAlphaId.ToString(),
            new UpdateRequestStatusRequest("resolved"),
            TestContext.Current.CancellationToken);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var payload = JsonSerializer.Serialize(badRequest.Value);
        Assert.Contains("not allowed to set that request status manually", payload);

        var request = await db.Requests.FirstAsync(x => x.Id == RequestAlphaId, TestContext.Current.CancellationToken);
        Assert.NotEqual("resolved", request.Status);
    }

    [Fact]
    public async Task UpdateStatus_ToOverdue_FromAccountant_IsRejected()
    {
        await using var db = BuildDb();
        Seed(db);

        var accountant = BuildUser(AccountantUserId, "accountant");
        var controller = new RequestsController(RequestService.CreateForTests(db))
        {
            ControllerContext = BuildControllerContext(accountant)
        };

        var result = await controller.UpdateStatus(
            RequestAlphaId.ToString(),
            new UpdateRequestStatusRequest("overdue"),
            TestContext.Current.CancellationToken);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var payload = JsonSerializer.Serialize(badRequest.Value);
        Assert.Contains("not allowed to set that request status manually", payload);

        var request = await db.Requests.FirstAsync(x => x.Id == RequestAlphaId, TestContext.Current.CancellationToken);
        Assert.NotEqual("overdue", request.Status);
    }

    private static ControllerContext BuildControllerContext(ClaimsPrincipal user)
    {
        return new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = user
            }
        };
    }

    private static ClaimsPrincipal BuildUser(Guid userId, string role, IEnumerable<Guid>? clientIds = null)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Role, role)
        };

        if (clientIds is not null)
        {
            claims.AddRange(clientIds.Select(x => new Claim("client_id", x.ToString())));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static PortalDbContext BuildDb()
    {
        var options = new DbContextOptionsBuilder<PortalDbContext>()
            .UseInMemoryDatabase($"phase3-test-{Guid.NewGuid():N}")
            .Options;
        return new PortalDbContext(options);
    }

    private static void Seed(PortalDbContext db)
    {
        db.Users.AddRange(
            BuildActiveUser(AccountantUserId, "Accountant", "acc@test.com", UserRole.Accountant),
            BuildActiveUser(AccountantTwoId, "Accountant Two", "acc2@test.com", UserRole.Accountant),
            BuildActiveUser(ClientUserId, "Client", "client@test.com", UserRole.Client, [ClientAlphaId]));

        var alpha = Client.Create(ClientAlphaId, "Alpha", "Pty Ltd", "A", "a@test.com", ClientStatus.Active);
        alpha.AssignAccountant(AccountantUserId);
        alpha.UpdateComplianceHealth(90);

        var beta = Client.Create(ClientBetaId, "Beta", "Pty Ltd", "B", "b@test.com", ClientStatus.Active);
        beta.AssignAccountant(AccountantTwoId);
        beta.UpdateComplianceHealth(80);

        db.Clients.AddRange(alpha, beta);
        db.ClientAssignments.Add(ClientAssignment.Create(Guid.NewGuid(), AccountantUserId, ClientAlphaId));

        db.Requests.AddRange(
            RequestItem.Create(RequestAlphaId, ClientAlphaId, "clarification_needed", null, "R1", "d1", RequestPriority.Medium, AccountantUserId, RequestStatus.Open, null),
            RequestItem.Create(RequestBetaId, ClientBetaId, "clarification_needed", null, "R2", "d2", RequestPriority.Medium, AccountantTwoId, RequestStatus.Open, null));

        db.RequestComments.Add(RequestComment.Create(
            Guid.Parse("89999999-9999-9999-9999-999999999991"),
            RequestAlphaId,
            ClientAlphaId,
            ClientUserId,
            "client",
            false,
            "Please review"));

        db.SaveChanges();
    }

    private static User BuildActiveUser(Guid id, string fullName, string email, UserRole role, IEnumerable<Guid>? clientIds = null)
    {
        var user = User.CreateInvited(
            id,
            fullName,
            email,
            role,
            "x",
            JsonSerializer.Serialize(clientIds?.Select(x => x.ToString()).ToArray() ?? Array.Empty<string>()),
            null);
        user.CompleteSetup(fullName, "x");
        return user;
    }
}


