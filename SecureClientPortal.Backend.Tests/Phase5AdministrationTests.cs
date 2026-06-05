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

public class Phase5AdministrationTests
{
    [Fact]
    public async Task AdminCanDisableEnableAndResetUserPassword()
    {
        await using var db = BuildDb();
        Seed(db);

        var admin = BuildAdminController(db);

        var disable = await admin.DisableUser("u_acc_001");
        Assert.IsType<OkObjectResult>(disable);
        var disabledUser = await db.Users.FirstAsync(x => x.Id == "u_acc_001");
        Assert.Contains("disabled", disabledUser.SecurityJson);

        var enable = await admin.EnableUser("u_acc_001");
        Assert.IsType<OkObjectResult>(enable);

        var reset = await admin.ResetPassword("u_acc_001", new AdminResetPasswordRequest(null, "rotation"));
        var resetOk = Assert.IsType<OkObjectResult>(reset);
        var resetJson = JsonSerializer.Serialize(resetOk.Value);
        Assert.Contains("\"reset\":true", resetJson);

        var auditActions = await db.AuditLogs.Select(x => x.Action).ToListAsync();
        Assert.Contains("users.status_changed", auditActions);
        Assert.Contains("users.password_reset", auditActions);
    }

    [Fact]
    public async Task AdminCanManageAssignmentsIncludingReassignAndPrimary()
    {
        await using var db = BuildDb();
        Seed(db);

        db.Users.Add(new User
        {
            Id = "u_acc_002",
            FullName = "Backup Accountant",
            Email = "backup@test.com",
            PasswordHash = "x",
            Role = "accountant",
            ClientIdsJson = "[]"
        });
        await db.SaveChangesAsync();

        var controller = new AssignmentsController(db)
        {
            ControllerContext = BuildControllerContext(BuildUser("u_admin_001", "admin"))
        };

        var create = await controller.Create(new CreateAssignmentRequest("u_acc_002", "c_001", false));
        Assert.IsType<CreatedResult>(create);

        var makePrimary = await controller.MakePrimary((await db.ClientAssignments.FirstAsync(x => x.AccountantUserId == "u_acc_002")).Id);
        Assert.IsType<OkObjectResult>(makePrimary);

        var client = await db.Clients.FirstAsync(x => x.Id == "c_001");
        Assert.Equal("u_acc_002", client.AssignedAccountantId);

        var reassign = await controller.Reassign(new ReassignAccountantRequest("c_001", "u_acc_002", "u_acc_001", true));
        Assert.IsType<OkObjectResult>(reassign);

        client = await db.Clients.FirstAsync(x => x.Id == "c_001");
        Assert.Equal("u_acc_001", client.AssignedAccountantId);
    }

    [Fact]
    public async Task FirmManagementStoresTemplatesAndRules()
    {
        await using var db = BuildDb();
        Seed(db);

        var controller = new FirmManagementController(db)
        {
            ControllerContext = BuildControllerContext(BuildUser("u_admin_001", "admin"))
        };

        var seedDefaults = await controller.SeedDefaults();
        Assert.IsType<OkObjectResult>(seedDefaults);

        var requiredDocs = await controller.GetRequiredDocumentTemplates();
        Assert.IsType<OkObjectResult>(requiredDocs.Result);

        var putRequests = await controller.PutRequestTemplates(
        [
            new RequestTemplateDto("r1", "Clarification", "clarification_needed", "Clarify {{item}}", "Need more detail", "medium", 2)
        ]);
        Assert.IsType<OkObjectResult>(putRequests);

        var putReminders = await controller.PutReminderRules(
        [
            new ReminderRuleDto("rr1", "2-day reminder", "deadline_approaching", 2, "client", "Due in 2 days", true)
        ]);
        Assert.IsType<OkObjectResult>(putReminders);

        var putDeadlines = await controller.PutDeadlineRules(
        [
            new DeadlineRuleDto("dr1", "Monthly", "monthly_pack", 5, 1, "high", true)
        ]);
        Assert.IsType<OkObjectResult>(putDeadlines);

        var putEscalations = await controller.PutEscalationRules(
        [
            new EscalationRuleDto("er1", "Escalate overdue", "overdue_client_action", 3, "accountant", "create_request", true)
        ]);
        Assert.IsType<OkObjectResult>(putEscalations);

        var savedKeys = await db.SystemSettings.Select(x => x.Key).ToListAsync();
        Assert.Contains("firm.request_templates", savedKeys);
        Assert.Contains("firm.reminder_rules", savedKeys);
        Assert.Contains("firm.deadline_rules", savedKeys);
        Assert.Contains("firm.escalation_rules", savedKeys);
    }

    private static AdminController BuildAdminController(PortalDbContext db)
    {
        return new AdminController(db)
        {
            ControllerContext = BuildControllerContext(BuildUser("u_admin_001", "admin"))
        };
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
            .UseInMemoryDatabase($"phase5-test-{Guid.NewGuid():N}")
            .Options;
        return new PortalDbContext(options);
    }

    private static void Seed(PortalDbContext db)
    {
        db.Users.AddRange(
            new User
            {
                Id = "u_admin_001",
                FullName = "Admin",
                Email = "admin@test.com",
                PasswordHash = "x",
                Role = "admin",
                ClientIdsJson = "[]"
            },
            new User
            {
                Id = "u_acc_001",
                FullName = "Accountant",
                Email = "acc@test.com",
                PasswordHash = "x",
                Role = "accountant",
                ClientIdsJson = "[]"
            });

        db.Clients.Add(new Client
        {
            Id = "c_001",
            Name = "Alpha",
            EntityType = "Pty Ltd",
            Status = "active",
            ComplianceHealth = 90,
            AssignedAccountantId = "u_acc_001",
            PrimaryContact = "A",
            Email = "a@test.com"
        });

        db.ClientAssignments.Add(new ClientAssignment
        {
            Id = "ca_001",
            AccountantUserId = "u_acc_001",
            ClientId = "c_001"
        });

        db.SaveChanges();
    }
}
