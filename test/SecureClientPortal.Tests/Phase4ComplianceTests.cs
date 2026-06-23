using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application.Contracts;
using SecureClientPortal.Backend.Controllers;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Infrastructure.Compliance.Application;
using SecureClientPortal.Backend.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;

namespace SecureClientPortal.Backend.Tests;

public class Phase4ComplianceTests
{
    private static readonly Guid AdminUserId = Guid.Parse("91111111-1111-1111-1111-111111111111");
    private static readonly Guid AccountantUserId = Guid.Parse("92222222-2222-2222-2222-222222222221");
    private static readonly Guid AccountantTwoId = Guid.Parse("92222222-2222-2222-2222-222222222222");
    private static readonly Guid ClientUserId = Guid.Parse("93333333-3333-3333-3333-333333333331");
    private static readonly Guid ClientAlphaId = Guid.Parse("9aaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1");
    private static readonly Guid ClientBetaId = Guid.Parse("9aaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2");
    private static readonly Guid TaxCategoryId = Guid.Parse("9ccccccc-cccc-cccc-cccc-ccccccccccc1");
    private static readonly Guid PopiaCategoryId = Guid.Parse("9ccccccc-cccc-cccc-cccc-ccccccccccc2");

    [Fact]
    public async Task ComplianceItems_AreScopedByAssignedClient()
    {
        await using var db = BuildDb();
        Seed(db);

        var accountant = BuildUser(AccountantUserId, "accountant");
        var controller = new ComplianceController(new ComplianceService(db))
        {
            ControllerContext = BuildControllerContext(accountant)
        };

        var result = await controller.GetItems(ct: TestContext.Current.CancellationToken);
        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);

        Assert.Contains(ClientAlphaId.ToString(), json);
        Assert.DoesNotContain(ClientBetaId.ToString(), json);
        Assert.Contains("Accountant", json);
        Assert.Contains("\"RiskLevel\":\"high\"", json);
    }

    [Fact]
    public async Task ComplianceAlerts_AndSummary_ReturnExpectedRiskSignals()
    {
        await using var db = BuildDb();
        Seed(db);

        var admin = BuildUser(AdminUserId, "admin");
        var controller = new ComplianceController(new ComplianceService(db))
        {
            ControllerContext = BuildControllerContext(admin)
        };

        var alertsResult = await controller.GetAlerts(ct: TestContext.Current.CancellationToken);
        var alertsOk = Assert.IsType<OkObjectResult>(alertsResult);
        var alertsJson = JsonSerializer.Serialize(alertsOk.Value);
        Assert.Contains("\"alertLevel\":\"high\"", alertsJson);
        Assert.Contains("\"alertLevel\":\"critical\"", alertsJson);

        var summaryResult = await controller.GetSummaryReport(ct: TestContext.Current.CancellationToken);
        var summaryOk = Assert.IsType<OkObjectResult>(summaryResult);
        var summaryJson = JsonSerializer.Serialize(summaryOk.Value);
        Assert.Contains("\"totalItems\":2", summaryJson);
        Assert.Contains("\"valid\":1", summaryJson);
        Assert.Contains("\"expired\":1", summaryJson);
        Assert.Contains("\"criticalRisk\":1", summaryJson);
        Assert.Contains("\"highRisk\":1", summaryJson);
    }

    [Fact]
    public async Task ComplianceReminder_CreatesNotification_AndItemSupportsOwnerRiskAndRequiredDocument()
    {
        await using var db = BuildDb();
        Seed(db);

        var accountant = BuildUser(AccountantUserId, "accountant");
        var controller = new ComplianceController(new ComplianceService(db))
        {
            ControllerContext = BuildControllerContext(accountant)
        };

        var createItem = await controller.CreateItem(
            new CreateComplianceItemRequest(
                ClientAlphaId,
                PopiaCategoryId,
                "POPIA Processing Register",
                "pending",
                AccountantUserId,
                "critical",
                "signed_documents",
                DateTime.UtcNow.AddDays(5),
                DateTime.UtcNow.AddDays(20)),
            TestContext.Current.CancellationToken);
        var createItemOk = Assert.IsType<OkObjectResult>(createItem);
        var createItemJson = JsonSerializer.Serialize(createItemOk.Value);
        Assert.Contains("Accountant", createItemJson);
        Assert.Contains("\"RequiredDocumentCategory\":\"signed_documents\"", createItemJson);
        Assert.Contains("\"RiskLevel\":\"critical\"", createItemJson);

        var createdItem = await db.ComplianceItems.FirstAsync(x => x.Name == "POPIA Processing Register", TestContext.Current.CancellationToken);

        var reminderResult = await controller.CreateReminder(
            new CreateComplianceReminderRequest(
                createdItem.Id,
                ClientUserId,
                "deadline_approaching",
                DateTime.UtcNow.AddDays(3)),
            TestContext.Current.CancellationToken);
        Assert.IsType<OkObjectResult>(reminderResult);

        var notifications = await db.Notifications.Where(x => x.UserId == ClientUserId).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Contains(notifications, x => x.Type == "compliance.reminder");
    }

    [Fact]
    public async Task SeedDefaultCategories_CreatesExpectedComplianceFamilies()
    {
        await using var db = BuildDb();
        Seed(db, includeCategories: false);

        var admin = BuildUser(AdminUserId, "admin");
        var controller = new ComplianceController(new ComplianceService(db))
        {
            ControllerContext = BuildControllerContext(admin)
        };

        var result = await controller.SeedDefaultCategories(TestContext.Current.CancellationToken);
        Assert.IsType<OkObjectResult>(result);

        var names = await db.ComplianceCategories.Select(x => x.Name).OrderBy(x => x).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Contains("Tax Compliance", names);
        Assert.Contains("CIPC Compliance", names);
        Assert.Contains("Payroll Compliance", names);
        Assert.Contains("POPIA Compliance", names);
    }

    private static ControllerContext BuildControllerContext(ClaimsPrincipal user)
    {
        return new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
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
            .UseInMemoryDatabase($"phase4-test-{Guid.NewGuid():N}")
            .Options;
        return new PortalDbContext(options);
    }

    private static void Seed(PortalDbContext db, bool includeCategories = true)
    {
        db.Users.AddRange(
            BuildActiveUser(AdminUserId, "Admin", "admin@test.com", UserRole.Admin),
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

        if (includeCategories)
        {
            db.ComplianceCategories.AddRange(
                ComplianceCategory.Create(TaxCategoryId, "Tax Compliance", "Tax filings and proofs", "TAX"),
                ComplianceCategory.Create(PopiaCategoryId, "POPIA Compliance", "Privacy controls and evidence", "POPIA"));
        }

        db.ComplianceItems.AddRange(
            ComplianceItem.Create(
                Guid.Parse("9ddddddd-dddd-dddd-dddd-ddddddddddd1"),
                ClientAlphaId,
                TaxCategoryId,
                "Tax PIN",
                ComplianceItemStatus.Valid,
                AccountantUserId,
                ComplianceRiskLevel.High,
                "tax_working_papers",
                DateTime.UtcNow.AddDays(14),
                DateTime.UtcNow.AddDays(7)),
            ComplianceItem.Create(
                Guid.Parse("9ddddddd-dddd-dddd-dddd-ddddddddddd2"),
                ClientBetaId,
                TaxCategoryId,
                "VAT Return",
                ComplianceItemStatus.Expired,
                AccountantTwoId,
                ComplianceRiskLevel.Critical,
                "compliance_record",
                null,
                DateTime.UtcNow.AddDays(-1)));

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


