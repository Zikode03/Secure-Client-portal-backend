using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application.Contracts.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Domain.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Infrastructure.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Models;
using System.Security.Claims;

namespace SecureClientPortal.Backend.Tests;

/// <summary>
/// Protects the business rules that allow every client to have a different monthly-pack profile.
/// These tests deliberately exercise the service layer rather than only checking DTO shapes.
/// </summary>
public class ClientMonthlyPackProfileTests
{
    [Fact]
    public async Task ClientRecurringItem_RequiresProfessionalApproval_BeforeFuturePackInheritance()
    {
        await using var db = BuildDb();
        var clientId = Guid.NewGuid();
        db.Clients.Add(BuildClient(clientId));
        var august = MonthlyPack.Create(Guid.NewGuid(), clientId, 2026, 8);
        db.MonthlyPacks.Add(august);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var service = new ClientMonthlyPackProfileService(db);
        var clientUser = BuildUser(Guid.NewGuid(), "client", clientId);

        var added = await service.AddItemAsync(
            clientId,
            new AddClientMonthlyPackItemRequest(
                "fuel_statement",
                "Fuel Card Statement",
                false,
                "every_month",
                null),
            clientUser,
            TestContext.Current.CancellationToken);

        Assert.False(added.Forbidden);
        Assert.NotNull(added.Value);
        Assert.NotNull(added.Value!.RecurringRequestId);

        var beforeApproval = await service.GetAsync(clientId, clientUser, TestContext.Current.CancellationToken);
        Assert.Single(beforeApproval.Value!.PendingRecurringItems);
        Assert.DoesNotContain(
            beforeApproval.Value.RecurringItems,
            item => item.Label == "Fuel Card Statement");

        var admin = BuildUser(Guid.NewGuid(), "admin");
        var approved = await service.ApproveRecurringAsync(
            clientId,
            added.Value.RecurringRequestId!.Value,
            admin,
            TestContext.Current.CancellationToken);

        Assert.Empty(approved.Value!.PendingRecurringItems);
        Assert.Contains(
            approved.Value.RecurringItems,
            item => item.Label == "Fuel Card Statement" && item.Source == "client_specific");

        // Future months inherit approved recurring items automatically.
        var september = MonthlyPack.Create(Guid.NewGuid(), clientId, 2026, 9);
        db.MonthlyPacks.Add(september);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        await service.ApplyProfileToPackAsync(clientId, september.Id, TestContext.Current.CancellationToken);

        Assert.Contains(
            await db.DocumentSlots.Where(x => x.MonthlyPackId == september.Id).ToListAsync(TestContext.Current.CancellationToken),
            slot => slot.Label == "Fuel Card Statement");
    }

    [Fact]
    public async Task ThisMonthOnlyItem_DoesNotLeakIntoFutureMonthlyPacks()
    {
        await using var db = BuildDb();
        var clientId = Guid.NewGuid();
        db.Clients.Add(BuildClient(clientId));
        var august = MonthlyPack.Create(Guid.NewGuid(), clientId, 2026, 8);
        db.MonthlyPacks.Add(august);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var service = new ClientMonthlyPackProfileService(db);
        var clientUser = BuildUser(Guid.NewGuid(), "client", clientId);

        await service.AddItemAsync(
            clientId,
            new AddClientMonthlyPackItemRequest(
                "vehicle_finance",
                "Vehicle Finance Statement",
                false,
                "this_month",
                null),
            clientUser,
            TestContext.Current.CancellationToken);

        Assert.Contains(
            await db.DocumentSlots.Where(x => x.MonthlyPackId == august.Id).ToListAsync(TestContext.Current.CancellationToken),
            slot => slot.Label == "Vehicle Finance Statement");

        var september = MonthlyPack.Create(Guid.NewGuid(), clientId, 2026, 9);
        db.MonthlyPacks.Add(september);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        await service.ApplyProfileToPackAsync(clientId, september.Id, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(
            await db.DocumentSlots.Where(x => x.MonthlyPackId == september.Id).ToListAsync(TestContext.Current.CancellationToken),
            slot => slot.Label == "Vehicle Finance Statement");
    }

    [Fact]
    public async Task NewPack_InheritsSelectedFirmTemplate_AndApprovedClientSpecificRequirements()
    {
        await using var db = BuildDb();
        var clientId = Guid.NewGuid();
        db.Clients.Add(BuildClient(clientId));

        var template = MonthlyPackTemplate.Create(
            Guid.NewGuid(),
            "Transport Client",
            "Baseline monthly records for transport businesses.",
            1);
        var bankRequirement = RequiredDocumentTemplate.Create(
            Guid.NewGuid(),
            "Bank Statement",
            "Monthly bank statement.",
            "bank_statement",
            true,
            5);
        db.MonthlyPackTemplates.Add(template);
        db.RequiredDocumentTemplates.Add(bankRequirement);
        db.MonthlyPackTemplateItems.Add(MonthlyPackTemplateItem.Create(
            Guid.NewGuid(),
            template.Id,
            bankRequirement.Id,
            1));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var profileService = new ClientMonthlyPackProfileService(db);
        var admin = BuildUser(Guid.NewGuid(), "admin");
        await profileService.UpdateAsync(
            clientId,
            new UpdateClientMonthlyPackProfileRequest(
                template.Id,
                [new ClientMonthlyPackProfileItemInput("fuel_statement", "Fuel Statement", true)]),
            admin,
            TestContext.Current.CancellationToken);

        var packService = new MonthlyPackService(db, profileService);
        var created = await packService.CreateAsync(
            new CreateMonthlyPackRequest(clientId, 2026, 10, "not_started"),
            admin,
            TestContext.Current.CancellationToken);

        var slots = await db.DocumentSlots
            .Where(x => x.MonthlyPackId == created.created.Id)
            .OrderBy(x => x.Label)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Contains(slots, slot => slot.Label == "Bank Statement" && slot.IsRequired);
        Assert.Contains(slots, slot => slot.Label == "Fuel Statement" && slot.IsRequired);
    }

    private static PortalDbContext BuildDb()
    {
        var options = new DbContextOptionsBuilder<PortalDbContext>()
            .UseInMemoryDatabase($"client-pack-profile-tests-{Guid.NewGuid():N}")
            .Options;
        return new PortalDbContext(options);
    }

    private static Client BuildClient(Guid id) =>
        Client.Create(
            id,
            "Profile Test Client",
            "Private Company",
            "Finance Contact",
            $"finance-{id:N}@example.test",
            ClientStatus.Active);

    private static ClaimsPrincipal BuildUser(Guid userId, string role, Guid? clientId = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Role, role),
            new("role_scope", role)
        };

        if (clientId.HasValue)
        {
            claims.Add(new Claim("client_id", clientId.Value.ToString()));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }
}
