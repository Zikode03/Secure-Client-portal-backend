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

public class Phase4ComplianceTests
{
    [Fact]
    public async Task ComplianceItems_AreScopedByAssignedClient()
    {
        await using var db = BuildDb();
        Seed(db);

        var accountant = BuildUser("u_acc_001", "accountant");
        var controller = new ComplianceController(db)
        {
            ControllerContext = BuildControllerContext(accountant)
        };

        var result = await controller.GetItems();
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var json = JsonSerializer.Serialize(ok.Value);

        Assert.Contains("\"ClientId\":\"c_001\"", json);
        Assert.DoesNotContain("\"ClientId\":\"c_002\"", json);
        Assert.Contains("\"ownerName\":\"Accountant\"", json);
        Assert.Contains("\"RiskLevel\":\"high\"", json);
    }

    [Fact]
    public async Task ComplianceAlerts_AndSummary_ReturnExpectedRiskSignals()
    {
        await using var db = BuildDb();
        Seed(db);

        var admin = BuildUser("u_admin_001", "admin");
        var controller = new ComplianceController(db)
        {
            ControllerContext = BuildControllerContext(admin)
        };

        var alertsResult = await controller.GetAlerts();
        var alertsOk = Assert.IsType<OkObjectResult>(alertsResult.Result);
        var alertsJson = JsonSerializer.Serialize(alertsOk.Value);
        Assert.Contains("\"alertLevel\":\"high\"", alertsJson);
        Assert.Contains("\"alertLevel\":\"critical\"", alertsJson);

        var summaryResult = await controller.GetSummaryReport();
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

        var accountant = BuildUser("u_acc_001", "accountant");
        var controller = new ComplianceController(db)
        {
            ControllerContext = BuildControllerContext(accountant)
        };

        var createItem = await controller.CreateItem(new CreateComplianceItemRequest(
            "c_001",
            "cc_popia",
            "POPIA Processing Register",
            "pending",
            "u_acc_001",
            "critical",
            "signed_documents",
            DateTime.UtcNow.AddDays(5),
            DateTime.UtcNow.AddDays(20)));
        var createItemOk = Assert.IsType<OkObjectResult>(createItem.Result);
        var createItemJson = JsonSerializer.Serialize(createItemOk.Value);
        Assert.Contains("\"ownerName\":\"Accountant\"", createItemJson);
        Assert.Contains("\"RequiredDocumentCategory\":\"signed_documents\"", createItemJson);
        Assert.Contains("\"RiskLevel\":\"critical\"", createItemJson);

        var createdItem = await db.ComplianceItems.FirstAsync(x => x.Name == "POPIA Processing Register");

        var reminderResult = await controller.CreateReminder(new CreateComplianceReminderRequest(
            createdItem.Id,
            "u_client_001",
            "deadline_approaching",
            DateTime.UtcNow.AddDays(3)));
        Assert.IsType<OkObjectResult>(reminderResult.Result);

        var notifications = await db.Notifications.Where(x => x.UserId == "u_client_001").ToListAsync();
        Assert.Contains(notifications, x => x.Type == "compliance.reminder");
    }

    [Fact]
    public async Task SeedDefaultCategories_CreatesExpectedComplianceFamilies()
    {
        await using var db = BuildDb();
        Seed(db, includeCategories: false);

        var admin = BuildUser("u_admin_001", "admin");
        var controller = new ComplianceController(db)
        {
            ControllerContext = BuildControllerContext(admin)
        };

        var result = await controller.SeedDefaultCategories();
        Assert.IsType<OkObjectResult>(result);

        var names = await db.ComplianceCategories.Select(x => x.Name).OrderBy(x => x).ToListAsync();
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
            .UseInMemoryDatabase($"phase4-test-{Guid.NewGuid():N}")
            .Options;
        return new PortalDbContext(options);
    }

    private static void Seed(PortalDbContext db, bool includeCategories = true)
    {
        db.Users.AddRange(
            new User { Id = "u_admin_001", Email = "admin@test.com", FullName = "Admin", Role = "admin", PasswordHash = "x", ClientIdsJson = "[]" },
            new User { Id = "u_acc_001", Email = "acc@test.com", FullName = "Accountant", Role = "accountant", PasswordHash = "x", ClientIdsJson = "[]" },
            new User { Id = "u_client_001", Email = "client@test.com", FullName = "Client", Role = "client", PasswordHash = "x", ClientIdsJson = "[\"c_001\"]" });

        db.Clients.AddRange(
            new Client { Id = "c_001", Name = "Alpha", EntityType = "Pty Ltd", Status = "active", ComplianceHealth = 90, AssignedAccountantId = "u_acc_001", PrimaryContact = "A", Email = "a@test.com" },
            new Client { Id = "c_002", Name = "Beta", EntityType = "Pty Ltd", Status = "active", ComplianceHealth = 80, AssignedAccountantId = "u_acc_002", PrimaryContact = "B", Email = "b@test.com" });

        db.ClientAssignments.Add(new ClientAssignment { Id = "ca_001", AccountantUserId = "u_acc_001", ClientId = "c_001" });

        if (includeCategories)
        {
            db.ComplianceCategories.AddRange(
                new ComplianceCategory
                {
                    Id = "cc_tax",
                    Name = "Tax Compliance",
                    Code = "TAX",
                    Description = "Tax filings and proofs",
                    IsActive = true
                },
                new ComplianceCategory
                {
                    Id = "cc_popia",
                    Name = "POPIA Compliance",
                    Code = "POPIA",
                    Description = "Privacy controls and evidence",
                    IsActive = true
                });
        }

        db.ComplianceItems.AddRange(
            new ComplianceItem
            {
                Id = "ci_001",
                ClientId = "c_001",
                CategoryId = "cc_tax",
                Name = "Tax PIN",
                Status = "valid",
                OwnerUserId = "u_acc_001",
                RiskLevel = "high",
                RequiredDocumentCategory = "tax_working_papers",
                DueDateUtc = DateTime.UtcNow.AddDays(14),
                ExpiryDateUtc = DateTime.UtcNow.AddDays(7)
            },
            new ComplianceItem
            {
                Id = "ci_002",
                ClientId = "c_002",
                CategoryId = "cc_tax",
                Name = "VAT Return",
                Status = "expired",
                OwnerUserId = "u_acc_002",
                RiskLevel = "critical",
                RequiredDocumentCategory = "compliance_record",
                ExpiryDateUtc = DateTime.UtcNow.AddDays(-1)
            });

        db.SaveChanges();
    }
}
