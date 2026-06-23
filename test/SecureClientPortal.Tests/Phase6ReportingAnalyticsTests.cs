using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Controllers;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Infrastructure.Reporting;
using SecureClientPortal.Backend.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;

namespace SecureClientPortal.Backend.Tests;

public class Phase6ReportingAnalyticsTests
{
    private static readonly Guid AdminUserId = Guid.Parse("a1111111-1111-1111-1111-111111111111");
    private static readonly Guid AccountantUserId = Guid.Parse("a2222222-2222-2222-2222-222222222222");
    private static readonly Guid ClientUserId = Guid.Parse("a3333333-3333-3333-3333-333333333333");
    private static readonly Guid ClientTwoUserId = Guid.Parse("a3333333-3333-3333-3333-333333333334");
    private static readonly Guid ClientAlphaId = Guid.Parse("abbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb1");
    private static readonly Guid ClientBetaId = Guid.Parse("abbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb2");
    private static readonly Guid TaxCategoryId = Guid.Parse("accccccc-cccc-cccc-cccc-ccccccccccc1");
    private static readonly Guid DocumentAlphaId = Guid.Parse("addddddd-dddd-dddd-dddd-ddddddddddd1");
    private static readonly Guid DocumentBetaId = Guid.Parse("addddddd-dddd-dddd-dddd-ddddddddddd2");

    [Fact]
    public async Task FirmReports_ReturnOverdueMissingOpenRequestAndRiskMetrics()
    {
        await using var db = BuildDb();
        Seed(db);

        var controller = new ReportsController(new ReportService(db))
        {
            ControllerContext = BuildControllerContext(BuildUser(AdminUserId, "admin"))
        };

        var result = await controller.GetFirmReports(TestContext.Current.CancellationToken);
        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);

        Assert.Contains("\"overdueClients\"", json);
        Assert.Contains("\"missingDocuments\"", json);
        Assert.Contains("\"openRequests\"", json);
        Assert.Contains("\"complianceRisk\"", json);
        Assert.Contains("\"totalOpenRequests\":2", json);
        Assert.Contains("\"totalHighRiskComplianceItems\":2", json);
    }

    [Fact]
    public async Task AccountantReports_ReturnWorkloadReviewTimeAndAssignedClients()
    {
        await using var db = BuildDb();
        Seed(db);

        var controller = new ReportsController(new ReportService(db))
        {
            ControllerContext = BuildControllerContext(BuildUser(AdminUserId, "admin"))
        };

        var result = await controller.GetAccountantReports(TestContext.Current.CancellationToken);
        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);

        Assert.Contains("\"assignedClients\":2", json);
        Assert.Contains("\"openTasks\":1", json);
        Assert.Contains("\"totalReviews\":1", json);
        Assert.Contains("\"averageHours\":", json);
    }

    [Fact]
    public async Task ClientReports_ReturnComplianceScoreSubmissionRateAndOutstandingItems()
    {
        await using var db = BuildDb();
        Seed(db);

        var controller = new ReportsController(new ReportService(db))
        {
            ControllerContext = BuildControllerContext(BuildUser(ClientUserId, "client", [ClientAlphaId]))
        };

        var result = await controller.GetClientReports(TestContext.Current.CancellationToken);
        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);

        Assert.Contains("\"complianceScore\":50", json);
        Assert.Contains("\"submissionRate\":100", json);
        Assert.Contains("\"openRequests\":1", json);
        Assert.DoesNotContain(ClientBetaId.ToString(), json);
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
            .UseInMemoryDatabase($"phase6-test-{Guid.NewGuid():N}")
            .Options;
        return new PortalDbContext(options);
    }

    private static void Seed(PortalDbContext db)
    {
        db.Users.AddRange(
            BuildActiveUser(AdminUserId, "Admin", "admin@test.com", UserRole.Admin),
            BuildActiveUser(AccountantUserId, "Accountant", "acc@test.com", UserRole.Accountant),
            BuildActiveUser(ClientUserId, "Client", "client@test.com", UserRole.Client, [ClientAlphaId]),
            BuildActiveUser(ClientTwoUserId, "Client Two", "client2@test.com", UserRole.Client, [ClientBetaId]));

        var alpha = Client.Create(ClientAlphaId, "Alpha", "Pty Ltd", "A", "a@test.com", ClientStatus.Active);
        alpha.AssignAccountant(AccountantUserId);
        alpha.UpdateComplianceHealth(90);

        var beta = Client.Create(ClientBetaId, "Beta", "Pty Ltd", "B", "b@test.com", ClientStatus.Active);
        beta.AssignAccountant(AccountantUserId);
        beta.UpdateComplianceHealth(70);

        db.Clients.AddRange(alpha, beta);
        db.ClientAssignments.AddRange(
            ClientAssignment.Create(Guid.NewGuid(), AccountantUserId, ClientAlphaId),
            ClientAssignment.Create(Guid.NewGuid(), AccountantUserId, ClientBetaId));

        var packAlpha = new MonthlyPack { Id = Guid.NewGuid(), ClientId = ClientAlphaId, Year = 2026, Month = 6 };
        packAlpha.Complete();
        var packBeta = new MonthlyPack { Id = Guid.NewGuid(), ClientId = ClientBetaId, Year = 2026, Month = 6 };
        db.MonthlyPacks.AddRange(packAlpha, packBeta);

        var docAlpha = Document.CreateUploaded(DocumentAlphaId, ClientAlphaId, packAlpha.Id, "Bank Statement", "bank_statement", null, "application/pdf", 123, "alpha.pdf", ClientUserId);
        docAlpha.MarkUnderReview();
        docAlpha.UpdateMetadata(docAlpha.Name, docAlpha.Category, DocumentStatus.UnderReview, docAlpha.SizeBytes, docAlpha.StorageKey);

        var docBeta = Document.CreateUploaded(DocumentBetaId, ClientBetaId, packBeta.Id, "Invoices", "invoices", null, "application/pdf", 123, "beta.pdf", ClientTwoUserId);
        db.Documents.AddRange(docAlpha, docBeta);

        db.ReviewDecisions.Add(ReviewDecision.Create(
            Guid.NewGuid(),
            DocumentAlphaId,
            "accepted",
            AccountantUserId,
            "accountant",
            null,
            null,
            DateTime.UtcNow.AddHours(-1)));

        db.Requests.AddRange(
            RequestItem.Create(Guid.NewGuid(), ClientAlphaId, "clarification_needed", null, "Clarify bank statement", "Need explanation", RequestPriority.High, AccountantUserId, RequestStatus.WaitingOnClient, DateTime.UtcNow.AddDays(-1)),
            RequestItem.Create(Guid.NewGuid(), ClientBetaId, "missing_document", null, "Upload invoices", "Missing", RequestPriority.Medium, AccountantUserId, RequestStatus.Open, DateTime.UtcNow.AddDays(2)));

        db.ComplianceCategories.Add(ComplianceCategory.Create(TaxCategoryId, "Tax Compliance", "Tax", "TAX"));

        db.ComplianceItems.AddRange(
            ComplianceItem.Create(Guid.NewGuid(), ClientAlphaId, TaxCategoryId, "VAT Return", ComplianceItemStatus.Valid, AccountantUserId, ComplianceRiskLevel.Medium, null, null, null),
            ComplianceItem.Create(Guid.NewGuid(), ClientAlphaId, TaxCategoryId, "Tax Clearance", ComplianceItemStatus.Missing, AccountantUserId, ComplianceRiskLevel.High, "compliance_record", null, null),
            ComplianceItem.Create(Guid.NewGuid(), ClientBetaId, TaxCategoryId, "PAYE", ComplianceItemStatus.Expired, AccountantUserId, ComplianceRiskLevel.Critical, "payroll_summary", null, DateTime.UtcNow.AddDays(-1)));

        db.Tasks.Add(TaskItem.Create(Guid.NewGuid(), ClientAlphaId, "Review docs", "todo", "high", null, AccountantUserId));

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

