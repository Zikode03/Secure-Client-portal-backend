using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Domain.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Infrastructure.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Models;
using System.Security.Claims;

namespace SecureClientPortal.Backend.Tests;

/// <summary>
/// Protects the Phase 2 rule that Industry describes business activity while EntityType only
/// describes legal form. Monthly-pack template recommendations must never confuse the two.
/// </summary>
public class IndustryAwareMonthlyPackRecommendationTests
{
    [Fact]
    public async Task Recommendation_UsesRecordedIndustry()
    {
        await using var db = BuildDb();
        var clientId = Guid.NewGuid();
        var client = Client.Create(
            clientId,
            "Transport Client",
            "Private Company",
            "Finance Contact",
            $"finance-{clientId:N}@example.test",
            ClientStatus.Active);
        client.UpdateBusinessProfile(
            client.Name,
            "",
            "",
            "",
            "",
            client.PrimaryContact,
            client.Email,
            "",
            "",
            "",
            "",
            "Transport & Logistics",
            "");
        db.Clients.Add(client);

        var transport = MonthlyPackTemplate.Create(
            Guid.NewGuid(),
            "Transport & Logistics",
            "Transport baseline.",
            1);
        var professional = MonthlyPackTemplate.Create(
            Guid.NewGuid(),
            "Professional Services",
            "Service baseline.",
            1);
        db.MonthlyPackTemplates.AddRange(transport, professional);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var inner = new ClientMonthlyPackProfileService(db);
        var service = new IndustryAwareClientMonthlyPackProfileService(inner, db);
        var result = await service.GetAsync(
            clientId,
            BuildAdmin(),
            TestContext.Current.CancellationToken);

        Assert.Equal(transport.Id, result.Value!.RecommendedTemplateId);
        Assert.Contains(
            "transport",
            result.Value.RecommendedTemplateReason!,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Recommendation_DoesNotUseEntityTypeAsIndustryFallback()
    {
        await using var db = BuildDb();
        var clientId = Guid.NewGuid();
        db.Clients.Add(Client.Create(
            clientId,
            "Legal Form Test Client",
            "Transport Business",
            "Finance Contact",
            $"finance-{clientId:N}@example.test",
            ClientStatus.Active));
        db.MonthlyPackTemplates.Add(MonthlyPackTemplate.Create(
            Guid.NewGuid(),
            "Transport & Logistics",
            "Transport baseline.",
            1));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var inner = new ClientMonthlyPackProfileService(db);
        var service = new IndustryAwareClientMonthlyPackProfileService(inner, db);
        var result = await service.GetAsync(
            clientId,
            BuildAdmin(),
            TestContext.Current.CancellationToken);

        Assert.Null(result.Value!.RecommendedTemplateId);
        Assert.Null(result.Value.RecommendedTemplateReason);
    }

    private static PortalDbContext BuildDb()
    {
        var options = new DbContextOptionsBuilder<PortalDbContext>()
            .UseInMemoryDatabase($"industry-aware-pack-tests-{Guid.NewGuid():N}")
            .Options;
        return new PortalDbContext(options);
    }

    private static ClaimsPrincipal BuildAdmin()
    {
        var userId = Guid.NewGuid();
        return new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Role, "admin"),
                new Claim("role_scope", "admin")
            ],
            "test"));
    }
}
