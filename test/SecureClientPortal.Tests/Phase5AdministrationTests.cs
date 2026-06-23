using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application;
using SecureClientPortal.Backend.Application.Contracts;
using SecureClientPortal.Backend.Application.Identity;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Controllers;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Infrastructure.FirmManagement.Application;
using SecureClientPortal.Backend.Infrastructure.Identity.Application;
using SecureClientPortal.Backend.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;

namespace SecureClientPortal.Backend.Tests;

public class Phase5AdministrationTests
{
    private static readonly Guid AdminUserId = Guid.Parse("d1111111-1111-1111-1111-111111111111");
    private static readonly Guid AccountantUserId = Guid.Parse("d2222222-2222-2222-2222-222222222221");
    private static readonly Guid BackupAccountantUserId = Guid.Parse("d2222222-2222-2222-2222-222222222222");
    private static readonly Guid ClientAlphaId = Guid.Parse("dddddddd-dddd-dddd-dddd-ddddddddddd1");

    [Fact]
    public async Task AdminCanDisableEnableAndResetUserPassword()
    {
        await using var db = BuildDb();
        Seed(db);

        var admin = BuildAdminController(db);

        var disable = await admin.DisableUser(AccountantUserId.ToString(), TestContext.Current.CancellationToken);
        Assert.IsType<OkObjectResult>(disable);
        var disabledUser = await db.Users.FirstAsync(x => x.Id == AccountantUserId, TestContext.Current.CancellationToken);
        Assert.Contains("disabled", disabledUser.SecurityJson);

        var enable = await admin.EnableUser(AccountantUserId.ToString(), TestContext.Current.CancellationToken);
        Assert.IsType<OkObjectResult>(enable);

        var reset = await admin.ResetPassword(AccountantUserId.ToString(), new AdminResetPasswordRequest(null, "rotation"), TestContext.Current.CancellationToken);
        var resetOk = Assert.IsType<OkObjectResult>(reset);
        var resetJson = JsonSerializer.Serialize(resetOk.Value);
        Assert.Contains("\"reset\":true", resetJson);

        var auditActions = await db.AuditLogs.Select(x => x.Action).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Contains("users.status_changed", auditActions);
        Assert.Contains("users.password_reset", auditActions);
    }

    [Fact]
    public async Task AdminCanManageAssignmentsIncludingReassignAndPrimary()
    {
        await using var db = BuildDb();
        Seed(db);

        db.Users.Add(BuildActiveUser(BackupAccountantUserId, "Backup Accountant", "backup@test.com", UserRole.Accountant));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var controller = new AssignmentsController(new AssignmentService(db), new CurrentUserContextFactory())
        {
            ControllerContext = BuildControllerContext(BuildUser(AdminUserId, "admin"))
        };

        var create = await controller.Create(new CreateAssignmentRequest(BackupAccountantUserId, ClientAlphaId, false));
        Assert.IsType<CreatedResult>(create);

        var backupAssignment = await db.ClientAssignments.FirstAsync(x => x.AccountantUserId == BackupAccountantUserId, TestContext.Current.CancellationToken);
        var makePrimary = await controller.MakePrimary(backupAssignment.Id.ToString());
        Assert.IsType<OkObjectResult>(makePrimary);

        var client = await db.Clients.FirstAsync(x => x.Id == ClientAlphaId, TestContext.Current.CancellationToken);
        Assert.Equal(BackupAccountantUserId, client.AssignedAccountantId);

        var reassign = await controller.Reassign(new ReassignAccountantRequest(ClientAlphaId, BackupAccountantUserId, AccountantUserId, true));
        Assert.IsType<OkObjectResult>(reassign);

        client = await db.Clients.FirstAsync(x => x.Id == ClientAlphaId, TestContext.Current.CancellationToken);
        Assert.Equal(AccountantUserId, client.AssignedAccountantId);
    }

    [Fact]
    public async Task FirmManagementStoresTemplatesAndRules()
    {
        await using var db = BuildDb();
        Seed(db);

        var controller = new FirmManagementController(new FirmManagementService(db), new CurrentUserContextFactory())
        {
            ControllerContext = BuildControllerContext(BuildUser(AdminUserId, "admin"))
        };

        var seedDefaults = await controller.SeedDefaults();
        Assert.IsType<OkObjectResult>(seedDefaults);

        var requiredDocs = await controller.GetRequiredDocumentTemplates();
        Assert.IsType<OkObjectResult>(requiredDocs.Result);

        var requestTemplateId = Guid.Parse("d6666666-6666-6666-6666-666666666661");
        var reminderRuleId = Guid.Parse("d6666666-6666-6666-6666-666666666662");
        var deadlineRuleId = Guid.Parse("d6666666-6666-6666-6666-666666666663");
        var escalationRuleId = Guid.Parse("d6666666-6666-6666-6666-666666666664");

        var putRequests = await controller.PutRequestTemplates(
        [
            new RequestTemplateDto(requestTemplateId, "Clarification", "clarification_needed", "Clarify {{item}}", "Need more detail", "medium", 2)
        ]);
        Assert.IsType<OkObjectResult>(putRequests);

        var putReminders = await controller.PutReminderRules(
        [
            new ReminderRuleDto(reminderRuleId, "2-day reminder", "deadline_approaching", 2, "client", "Due in 2 days", true)
        ]);
        Assert.IsType<OkObjectResult>(putReminders);

        var putDeadlines = await controller.PutDeadlineRules(
        [
            new DeadlineRuleDto(deadlineRuleId, "Monthly", "monthly_pack", 5, 1, "high", true)
        ]);
        Assert.IsType<OkObjectResult>(putDeadlines);

        var putEscalations = await controller.PutEscalationRules(
        [
            new EscalationRuleDto(escalationRuleId, "Escalate overdue", "overdue_client_action", 3, "accountant", "create_request", true)
        ]);
        Assert.IsType<OkObjectResult>(putEscalations);

        Assert.True(await db.RequiredDocumentTemplates.AnyAsync(TestContext.Current.CancellationToken));
        Assert.Single(await db.RequestTemplates.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Single(await db.ReminderRules.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Single(await db.DeadlineRules.ToListAsync(TestContext.Current.CancellationToken));
        Assert.True(await db.MonthlyPackTemplates.AnyAsync(TestContext.Current.CancellationToken));
        Assert.True(await db.MonthlyPackTemplateItems.AnyAsync(TestContext.Current.CancellationToken));

        var savedKeys = await db.SystemSettings.Select(x => x.Key).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Contains("firm.escalation_rules", savedKeys);
    }

    [Fact]
    public async Task RolesPersistPermissionCatalogAndLinks()
    {
        await using var db = BuildDb();
        Seed(db);

        var controller = new RolesController(new RoleService(db), new CurrentUserContextFactory())
        {
            ControllerContext = BuildControllerContext(BuildUser(AdminUserId, "admin"))
        };

        var created = await controller.Create(new CreateRoleRequest("review manager", "Review Manager", "accountant", ["documents.read", "documents.review"]));
        var createdResult = Assert.IsType<CreatedResult>(created);
        var json = JsonSerializer.Serialize(createdResult.Value);
        Assert.Contains("\"permissions\":[\"documents.read\",\"documents.review\"]", json);

        var permissions = await db.Permissions.OrderBy(x => x.Key).Select(x => x.Key).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Contains("documents.read", permissions);
        Assert.Contains("documents.review", permissions);

        var roleLinks = await db.RolePermissions.Where(x => x.RoleName == "review_manager").Select(x => x.PermissionKey).OrderBy(x => x).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["documents.read", "documents.review"], roleLinks);
    }

    private static AdminController BuildAdminController(PortalDbContext db)
    {
        return new AdminController(new AdminService(db, new FakeAccessEmailSender(), new FakeAccessLinkBuilder()))
        {
            ControllerContext = BuildControllerContext(BuildUser(AdminUserId, "admin"))
        };
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
            .UseInMemoryDatabase($"phase5-test-{Guid.NewGuid():N}")
            .Options;
        return new PortalDbContext(options);
    }

    private static void Seed(PortalDbContext db)
    {
        SeedRoles(db);

        db.Users.AddRange(
            BuildActiveUser(AdminUserId, "Admin", "admin@test.com", UserRole.Admin),
            BuildActiveUser(AccountantUserId, "Accountant", "acc@test.com", UserRole.Accountant));

        var client = Client.Create(ClientAlphaId, "Alpha", "Pty Ltd", "A", "a@test.com", ClientStatus.Active);
        client.AssignAccountant(AccountantUserId);
        client.UpdateComplianceHealth(90);
        db.Clients.Add(client);

        db.ClientAssignments.Add(ClientAssignment.Create(Guid.NewGuid(), AccountantUserId, ClientAlphaId));

        db.SaveChanges();
    }

    private static void SeedRoles(PortalDbContext db)
    {
        db.RoleDefinitions.AddRange(
            RoleDefinition.Create("admin", "Admin", "admin", RolePermissions.SerializePermissions(RolePermissions.ForRole("admin")), true),
            RoleDefinition.Create("accountant", "Accountant", "accountant", RolePermissions.SerializePermissions(RolePermissions.ForRole("accountant")), true),
            RoleDefinition.Create("client", "Client", "client", RolePermissions.SerializePermissions(RolePermissions.ForRole("client")), true));
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

    private sealed class FakeAccessEmailSender : IAccessEmailSender
    {
        public Task<AccessEmailDispatchResult> SendInviteAsync(string recipientEmail, string recipientName, string setupUrl, DateTime expiresAtUtc, CancellationToken ct)
            => Task.FromResult(new AccessEmailDispatchResult("test"));

        public Task<AccessEmailDispatchResult> SendPasswordResetAsync(string recipientEmail, string recipientName, string setupUrl, DateTime expiresAtUtc, CancellationToken ct)
            => Task.FromResult(new AccessEmailDispatchResult("test"));
    }

    private sealed class FakeAccessLinkBuilder : IAccessLinkBuilder
    {
        public string BuildPasswordResetUrl(string email, string token) => $"https://example.test/reset?email={email}&token={token}";
        public string BuildSetupUrl(string email, string token) => $"https://example.test/setup?email={email}&token={token}";
    }
}


