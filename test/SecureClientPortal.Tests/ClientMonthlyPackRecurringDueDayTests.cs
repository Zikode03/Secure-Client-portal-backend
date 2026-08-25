using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application.Contracts.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Domain.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Infrastructure.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Models;
using System.Security.Claims;

namespace SecureClientPortal.Backend.Tests;

/// <summary>
/// Protects recurring due-day behavior so client-specific deadlines survive into future months.
/// </summary>
public class ClientMonthlyPackRecurringDueDayTests
{
    [Fact]
    public async Task ApprovedRecurringItem_RecreatesDueDateInFutureMonth()
    {
        await using var db = BuildDb();
        var clientId = Guid.NewGuid();
        db.Clients.Add(Client.Create(
            clientId,
            "Transport Test Client",
            "Pty Ltd",
            "Finance Contact",
            "finance@test.local",
            ClientStatus.Active));

        var august = MonthlyPack.Create(Guid.NewGuid(), clientId, 2026, 8);
        db.MonthlyPacks.Add(august);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var service = new ClientMonthlyPackProfileService(db);
        var admin = BuildUser(Guid.NewGuid(), "admin");

        await service.AddItemAsync(
            clientId,
            new AddClientMonthlyPackItemRequest(
                "fuel_statement",
                "Fuel Statement",
                true,
                "every_month",
                new DateTime(2026, 8, 5, 23, 59, 59, DateTimeKind.Utc)),
            admin,
            TestContext.Current.CancellationToken);

        var september = MonthlyPack.Create(Guid.NewGuid(), clientId, 2026, 9);
        db.MonthlyPacks.Add(september);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await service.ApplyProfileToPackAsync(clientId, september.Id, TestContext.Current.CancellationToken);

        var inherited = await db.DocumentSlots.SingleAsync(
            x => x.MonthlyPackId == september.Id && x.Label == "Fuel Statement",
            TestContext.Current.CancellationToken);

        Assert.NotNull(inherited.DueDateUtc);
        Assert.Equal(5, inherited.DueDateUtc!.Value.Day);
        Assert.Equal(9, inherited.DueDateUtc.Value.Month);
        Assert.True(inherited.IsRequired);
    }

    private static PortalDbContext BuildDb()
    {
        var options = new DbContextOptionsBuilder<PortalDbContext>()
            .UseInMemoryDatabase($"recurring-due-day-{Guid.NewGuid():N}")
            .Options;
        return new PortalDbContext(options);
    }

    private static ClaimsPrincipal BuildUser(Guid userId, string role)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, role),
            new Claim("role_scope", role),
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }
}
