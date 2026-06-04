using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Controllers;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;

namespace SecureClientPortal.Backend.Tests;

public class Phase6ReportingAnalyticsTests
{
    [Fact]
    public async Task FirmReports_ReturnOverdueMissingOpenRequestAndRiskMetrics()
    {
        await using var db = BuildDb();
        Seed(db);

        var controller = new ReportsController(db)
        {
            ControllerContext = BuildControllerContext(BuildUser("u_admin_001", "admin"))
        };

        var result = await controller.GetFirmReports();
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

        var controller = new ReportsController(db)
        {
            ControllerContext = BuildControllerContext(BuildUser("u_admin_001", "admin"))
        };

        var result = await controller.GetAccountantReports();
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

        var controller = new ReportsController(db)
        {
            ControllerContext = BuildControllerContext(BuildUser("u_client_001", "client", ["c_001"]))
        };

        var result = await controller.GetClientReports();
        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);

        Assert.Contains("\"complianceScore\":50", json);
        Assert.Contains("\"submissionRate\":100", json);
        Assert.Contains("\"openRequests\":1", json);
        Assert.DoesNotContain("\"Id\":\"c_002\"", json);
    }

    private static ControllerContext BuildControllerContext(ClaimsPrincipal user)
    {
        return new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };
    }

    private static ClaimsPrincipal BuildUser(string userId, string role, IEnumerable<string>? clientIds = null)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId),
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Role, role)
        };

        if (clientIds is not null)
        {
            claims.AddRange(clientIds.Select(x => new Claim("client_id", x)));
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
            new User { Id = "u_admin_001", FullName = "Admin", Email = "admin@test.com", PasswordHash = "x", Role = "admin", ClientIdsJson = "[]" },
            new User { Id = "u_acc_001", FullName = "Accountant", Email = "acc@test.com", PasswordHash = "x", Role = "accountant", ClientIdsJson = "[]" },
            new User { Id = "u_client_001", FullName = "Client", Email = "client@test.com", PasswordHash = "x", Role = "client", ClientIdsJson = "[\"c_001\"]" });

        db.Clients.AddRange(
            new Client { Id = "c_001", Name = "Alpha", EntityType = "Pty Ltd", Status = "active", ComplianceHealth = 90, AssignedAccountantId = "u_acc_001", PrimaryContact = "A", Email = "a@test.com" },
            new Client { Id = "c_002", Name = "Beta", EntityType = "Pty Ltd", Status = "active", ComplianceHealth = 70, AssignedAccountantId = "u_acc_001", PrimaryContact = "B", Email = "b@test.com" });

        db.ClientAssignments.AddRange(
            new ClientAssignment { Id = "ca_001", AccountantUserId = "u_acc_001", ClientId = "c_001" },
            new ClientAssignment { Id = "ca_002", AccountantUserId = "u_acc_001", ClientId = "c_002" });

        db.MonthlyPacks.AddRange(
            new MonthlyPack { Id = "mp_001", ClientId = "c_001", Year = 2026, Month = 6, Status = "completed" },
            new MonthlyPack { Id = "mp_002", ClientId = "c_002", Year = 2026, Month = 6, Status = "draft" });

        db.Documents.AddRange(
            new Document
            {
                Id = "doc_001",
                ClientId = "c_001",
                Name = "Bank Statement",
                Category = "bank_statement",
                Status = "under_review",
                SizeBytes = 123,
                UploadedByUserId = "u_client_001",
                UploadedAtUtc = DateTime.UtcNow.AddHours(-6)
            },
            new Document
            {
                Id = "doc_002",
                ClientId = "c_002",
                Name = "Invoices",
                Category = "invoices",
                Status = "pending",
                SizeBytes = 123,
                UploadedByUserId = "u_client_002",
                UploadedAtUtc = DateTime.UtcNow.AddHours(-3)
            });

        db.ReviewDecisions.Add(new ReviewDecision
        {
            Id = "rd_001",
            DocumentId = "doc_001",
            Decision = "accepted",
            ReviewerUserId = "u_acc_001",
            ReviewerRole = "accountant",
            DecidedAtUtc = DateTime.UtcNow.AddHours(-1)
        });

        db.Requests.AddRange(
            new RequestItem
            {
                Id = "req_001",
                ClientId = "c_001",
                RequestType = "clarification",
                Title = "Clarify bank statement",
                Description = "Need explanation",
                Priority = "high",
                Status = "awaiting_client",
                DueDateUtc = DateTime.UtcNow.AddDays(-1),
                RequestedByUserId = "u_acc_001"
            },
            new RequestItem
            {
                Id = "req_002",
                ClientId = "c_002",
                RequestType = "missing_document",
                Title = "Upload invoices",
                Description = "Missing",
                Priority = "medium",
                Status = "open",
                DueDateUtc = DateTime.UtcNow.AddDays(2),
                RequestedByUserId = "u_acc_001"
            });

        db.ComplianceCategories.Add(new ComplianceCategory
        {
            Id = "cc_tax",
            Name = "Tax Compliance",
            Code = "TAX",
            Description = "Tax"
        });

        db.ComplianceItems.AddRange(
            new ComplianceItem
            {
                Id = "ci_001",
                ClientId = "c_001",
                CategoryId = "cc_tax",
                Name = "VAT Return",
                Status = "valid",
                RiskLevel = "medium",
                LinkedDocumentId = "doc_001"
            },
            new ComplianceItem
            {
                Id = "ci_002",
                ClientId = "c_001",
                CategoryId = "cc_tax",
                Name = "Tax Clearance",
                Status = "missing",
                RiskLevel = "high",
                RequiredDocumentCategory = "compliance_record"
            },
            new ComplianceItem
            {
                Id = "ci_003",
                ClientId = "c_002",
                CategoryId = "cc_tax",
                Name = "PAYE",
                Status = "expired",
                RiskLevel = "critical",
                RequiredDocumentCategory = "payroll_summary"
            });

        db.Tasks.Add(new TaskItem
        {
            Id = "task_001",
            ClientId = "c_001",
            Title = "Review docs",
            Status = "todo",
            Priority = "high",
            CreatedByUserId = "u_acc_001"
        });

        db.SaveChanges();
    }
}
