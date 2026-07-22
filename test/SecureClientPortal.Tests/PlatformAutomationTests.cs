using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Infrastructure.Modules.Platform;
using SecureClientPortal.Backend.Models;
using System.Text.Json;

namespace SecureClientPortal.Backend.Tests;

public class PlatformAutomationTests
{
    private static readonly Guid AdminUserId = Guid.Parse("f1111111-1111-1111-1111-111111111111");
    private static readonly Guid AccountantUserId = Guid.Parse("f2222222-2222-2222-2222-222222222222");
    private static readonly Guid ClientUserId = Guid.Parse("f3333333-3333-3333-3333-333333333333");
    private static readonly Guid ClientAlphaId = Guid.Parse("f4444444-4444-4444-4444-444444444444");
    private static readonly DateTime RunAtUtc = new(2026, 7, 21, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Automation_Run_CreatesPacks_Reminders_AndEscalations_WithoutDuplicates()
    {
        await using var db = BuildDb();
        Seed(db);

        var service = new AutomationWorkflowService(db);

        var firstRun = await service.RunAsync(RunAtUtc, TestContext.Current.CancellationToken);

        Assert.Equal(1, firstRun.MonthlyPacksCreated);
        Assert.Equal(2, firstRun.DocumentSlotsCreated);
        Assert.Equal(1, firstRun.MonthlyPackNotificationsSent);
        Assert.Equal(1, firstRun.MonthlyPackDeadlineNotificationsSent);
        Assert.Equal(2, firstRun.OverdueRequestsMarked);
        Assert.Equal(2, firstRun.RequestEscalationNotificationsSent);
        Assert.Equal(1, firstRun.ComplianceItemsUpdated);
        Assert.Equal(1, firstRun.ComplianceReminderNotificationsSent);

        var pack = await db.MonthlyPacks.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2026, pack.Year);
        Assert.Equal(7, pack.Month);

        var slots = await db.DocumentSlots
            .Where(x => x.MonthlyPackId == pack.Id)
            .OrderBy(x => x.Label)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, slots.Count);
        Assert.All(slots, x => Assert.Equal(new DateTime(2026, 7, 28, 0, 0, 0, DateTimeKind.Utc), x.DueDateUtc));

        var requests = await db.Requests.OrderBy(x => x.Title).ToListAsync(TestContext.Current.CancellationToken);
        Assert.All(requests, x => Assert.Equal("overdue", x.Status));

        var complianceItem = await db.ComplianceItems.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("expiring_soon", complianceItem.Status);

        var notifications = await db.Notifications
            .OrderBy(x => x.Type)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Contains(notifications, x => x.UserId == ClientUserId && x.Type == "monthly_pack.created");
        Assert.Contains(notifications, x => x.UserId == ClientUserId && x.Type == "monthly_pack.deadline_approaching");
        Assert.Contains(notifications, x => x.UserId == ClientUserId && x.Type == "compliance.deadline_approaching");
        Assert.Contains(notifications, x => x.UserId == AccountantUserId && x.Type == "request.escalation");
        Assert.Contains(notifications, x => x.UserId == AdminUserId && x.Type == "request.escalation");

        var complianceReminders = await db.ComplianceReminders.ToListAsync(TestContext.Current.CancellationToken);
        Assert.Single(complianceReminders);
        Assert.Equal("sent", complianceReminders[0].Status);

        var secondRun = await service.RunAsync(RunAtUtc.AddHours(1), TestContext.Current.CancellationToken);

        Assert.Equal(0, secondRun.MonthlyPacksCreated);
        Assert.Equal(0, secondRun.DocumentSlotsCreated);
        Assert.Equal(0, secondRun.MonthlyPackNotificationsSent);
        Assert.Equal(0, secondRun.MonthlyPackDeadlineNotificationsSent);
        Assert.Equal(0, secondRun.RequestEscalationNotificationsSent);
        Assert.Equal(0, secondRun.ComplianceReminderNotificationsSent);

        Assert.Equal(1, await db.MonthlyPacks.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, await db.DocumentSlots.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(notifications.Count, await db.Notifications.CountAsync(TestContext.Current.CancellationToken));
    }

    private static PortalDbContext BuildDb()
    {
        var options = new DbContextOptionsBuilder<PortalDbContext>()
            .UseInMemoryDatabase($"platform-automation-{Guid.NewGuid():N}")
            .Options;
        return new PortalDbContext(options);
    }

    private static void Seed(PortalDbContext db)
    {
        db.Users.AddRange(
            BuildActiveUser(AdminUserId, "Admin", "admin@test.com", UserRole.Admin),
            BuildActiveUser(AccountantUserId, "Accountant", "accountant@test.com", UserRole.Accountant),
            BuildActiveUser(ClientUserId, "Client", "client@test.com", UserRole.Client, [ClientAlphaId]));

        var client = Client.Create(ClientAlphaId, "Alpha", "Pty Ltd", "Primary", "alpha@test.com", ClientStatus.Active);
        client.AssignAccountant(AccountantUserId);
        client.UpdateComplianceHealth(85);
        db.Clients.Add(client);
        db.ClientAssignments.Add(ClientAssignment.Create(Guid.NewGuid(), AccountantUserId, ClientAlphaId));

        var bankTemplateId = Guid.Parse("f5555555-5555-5555-5555-555555555551");
        var invoiceTemplateId = Guid.Parse("f5555555-5555-5555-5555-555555555552");
        var packTemplateId = Guid.Parse("f6666666-6666-6666-6666-666666666661");

        db.RequiredDocumentTemplates.AddRange(
            RequiredDocumentTemplate.Create(bankTemplateId, "Bank Statement", "Monthly bank statement", "bank_statement", true, 28, true),
            RequiredDocumentTemplate.Create(invoiceTemplateId, "Invoices", "Monthly invoices", "invoices", true, 28, true));
        db.MonthlyPackTemplates.Add(MonthlyPackTemplate.Create(packTemplateId, "Default", "Default monthly template", 1, true));
        db.MonthlyPackTemplateItems.AddRange(
            MonthlyPackTemplateItem.Create(Guid.NewGuid(), packTemplateId, bankTemplateId, 1),
            MonthlyPackTemplateItem.Create(Guid.NewGuid(), packTemplateId, invoiceTemplateId, 2));

        db.ReminderRules.Add(ReminderRule.Create(
            Guid.Parse("f7777777-7777-7777-7777-777777777771"),
            "7-day reminder",
            "deadline_approaching",
            7,
            "client",
            "Deadline is approaching.",
            true));
        db.DeadlineRules.Add(DeadlineRule.Create(
            Guid.Parse("f8888888-8888-8888-8888-888888888881"),
            "Monthly pack due",
            "monthly_pack",
            28,
            0,
            "high",
            true));

        db.SystemSettings.Add(SystemSetting.Create(
            "firm.escalation_rules",
            JsonSerializer.Serialize(new[]
            {
                new EscalationRuleDto(Guid.NewGuid(), "Client overdue", "overdue_client_action", 2, "accountant", "create_request", true),
                new EscalationRuleDto(Guid.NewGuid(), "Accountant overdue", "overdue_accountant_action", 5, "admin", "notify_admin", true)
            })));

        db.Requests.AddRange(
            RequestItem.Create(
                Guid.Parse("f9999999-9999-9999-9999-999999999991"),
                ClientAlphaId,
                "reupload_required",
                null,
                "Waiting on client",
                "Needs client follow-up",
                RequestPriority.High,
                AccountantUserId,
                RequestStatus.WaitingOnClient,
                RunAtUtc.AddDays(-3)),
            RequestItem.Create(
                Guid.Parse("f9999999-9999-9999-9999-999999999992"),
                ClientAlphaId,
                "clarification_needed",
                null,
                "Waiting on accountant",
                "Needs accountant follow-up",
                RequestPriority.Medium,
                ClientUserId,
                RequestStatus.WaitingOnAccountant,
                RunAtUtc.AddDays(-6)));

        var categoryId = Guid.Parse("fa000000-0000-0000-0000-000000000010");
        db.ComplianceCategories.Add(ComplianceCategory.Create(categoryId, "Tax", "Tax matters", "TAX", true));
        db.ComplianceItems.Add(ComplianceItem.Create(
            Guid.Parse("fa000000-0000-0000-0000-000000000001"),
            ClientAlphaId,
            categoryId,
            "VAT Certificate",
            ComplianceItemStatus.Valid,
            AccountantUserId,
            ComplianceRiskLevel.High,
            "compliance_record",
            RunAtUtc.AddDays(7),
            RunAtUtc.AddDays(7),
            RunAtUtc.AddDays(-30)));

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
