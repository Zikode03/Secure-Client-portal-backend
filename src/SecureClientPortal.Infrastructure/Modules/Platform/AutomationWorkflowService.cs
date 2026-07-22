using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application.Contracts.Modules.FirmManagement;
using SecureClientPortal.Backend.Application.Modules.Platform;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Domain.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Domain.Modules.Requests;
using SecureClientPortal.Backend.Domain.Shared.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Domain.Shared.Modules.Requests;
using SecureClientPortal.Backend.Models;
using System.Text.Json;

namespace SecureClientPortal.Backend.Infrastructure.Modules.Platform;

public sealed class AutomationWorkflowService : IAutomationWorkflowService
{
    private const string SystemActorRole = "system";
    private const string EscalationRulesKey = "firm.escalation_rules";

    private readonly PortalDbContext _db;

    public AutomationWorkflowService(PortalDbContext db)
    {
        _db = db;
    }

    public async Task<AutomationRunSummary> RunAsync(DateTime? utcNow = null, CancellationToken ct = default)
    {
        var now = utcNow?.ToUniversalTime() ?? DateTime.UtcNow;

        var packResult = await AutoCreateMonthlyPacksAsync(now, ct);
        var monthlyReminderNotifications = await SendMonthlyPackDeadlineRemindersAsync(now, ct);
        var overdueResult = await ProcessOverdueRequestEscalationsAsync(now, ct);
        var complianceResult = await ProcessComplianceDeadlinesAsync(now, ct);

        return new AutomationRunSummary(
            now,
            packResult.MonthlyPacksCreated,
            packResult.DocumentSlotsCreated,
            packResult.NotificationsSent,
            monthlyReminderNotifications,
            overdueResult.OverdueRequestsMarked,
            overdueResult.NotificationsSent,
            complianceResult.ComplianceItemsUpdated,
            complianceResult.NotificationsSent);
    }

    private async Task<(int MonthlyPacksCreated, int DocumentSlotsCreated, int NotificationsSent)> AutoCreateMonthlyPacksAsync(DateTime now, CancellationToken ct)
    {
        var templates = await _db.MonthlyPackTemplates
            .Where(x => x.IsActive && x.AutoCreateDayOfMonth <= now.Day)
            .OrderBy(x => x.AutoCreateDayOfMonth)
            .ThenBy(x => x.Name)
            .ToListAsync(ct);
        if (templates.Count == 0)
        {
            return (0, 0, 0);
        }

        var templateIds = templates.Select(x => x.Id).ToArray();
        var templateItems = await _db.MonthlyPackTemplateItems
            .Where(x => templateIds.Contains(x.MonthlyPackTemplateId))
            .OrderBy(x => x.SortOrder)
            .ToListAsync(ct);
        var requiredTemplateIds = templateItems.Select(x => x.RequiredDocumentTemplateId).Distinct().ToArray();
        var requiredTemplates = await _db.RequiredDocumentTemplates
            .Where(x => requiredTemplateIds.Contains(x.Id) && x.IsActive)
            .ToDictionaryAsync(x => x.Id, ct);

        var monthlyDeadlineRule = await _db.DeadlineRules
            .Where(x => x.IsEnabled && x.Scope == "monthly_pack")
            .OrderBy(x => x.DueDayOfMonth)
            .FirstOrDefaultAsync(ct);

        var clients = await _db.Clients
            .Where(x => x.Status == "active")
            .OrderBy(x => x.Name)
            .ToListAsync(ct);

        var createdPacks = new List<MonthlyPack>();
        var createdSlots = 0;

        foreach (var client in clients)
        {
            var pack = await _db.MonthlyPacks.FirstOrDefaultAsync(
                x => x.ClientId == client.Id && x.Year == now.Year && x.Month == now.Month,
                ct);

            if (pack is null)
            {
                pack = MonthlyPack.Create(Guid.NewGuid(), client.Id, now.Year, now.Month, now);
                _db.MonthlyPacks.Add(pack);
                createdPacks.Add(pack);
            }

            var existingSlots = await _db.DocumentSlots
                .Where(x => x.MonthlyPackId == pack.Id)
                .ToListAsync(ct);
            var existingCategories = existingSlots
                .Select(x => x.Category)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var template in templates)
            {
                foreach (var item in templateItems.Where(x => x.MonthlyPackTemplateId == template.Id))
                {
                    if (!requiredTemplates.TryGetValue(item.RequiredDocumentTemplateId, out var requiredTemplate))
                    {
                        continue;
                    }

                    if (existingCategories.Contains(requiredTemplate.DocumentCategory))
                    {
                        continue;
                    }

                    var dueDay = requiredTemplate.DefaultDueDayOfMonth ?? monthlyDeadlineRule?.DueDayOfMonth;
                    DateTime? dueDate = dueDay.HasValue
                        ? BuildUtcDate(now.Year, now.Month, dueDay.Value)
                        : null;

                    var slot = DocumentSlot.Create(
                        Guid.NewGuid(),
                        pack.Id,
                        client.Id,
                        requiredTemplate.DocumentCategory,
                        requiredTemplate.Name,
                        requiredTemplate.IsRequired,
                        dueDate,
                        now);
                    _db.DocumentSlots.Add(slot);
                    existingCategories.Add(requiredTemplate.DocumentCategory);
                    createdSlots++;
                }
            }
        }

        if (createdPacks.Count > 0 || createdSlots > 0)
        {
            await _db.SaveChangesAsync(ct);
        }

        var notificationsSent = 0;
        foreach (var pack in createdPacks)
        {
            notificationsSent += await SendNotificationIfMissingAsync(
                pack.ClientId,
                "client",
                "monthly_pack.created",
                "Monthly pack created",
                $"A monthly pack for {pack.Year:D4}-{pack.Month:D2} is ready for document collection.",
                $"/monthly-packs/{pack.Id}",
                new { monthlyPackId = pack.Id, pack.Year, pack.Month },
                now,
                ct);

            await _db.WriteAuditLogAsync(
                actorUserId: null,
                actorRole: SystemActorRole,
                action: "monthly_packs.auto_created",
                entityType: "monthly_pack",
                entityId: pack.Id,
                clientId: pack.ClientId,
                metadataJson: JsonSerializer.Serialize(new { pack.ClientId, pack.Year, pack.Month }),
                ct);
        }

        return (createdPacks.Count, createdSlots, notificationsSent);
    }

    private async Task<int> SendMonthlyPackDeadlineRemindersAsync(DateTime now, CancellationToken ct)
    {
        var deadlineRule = await _db.DeadlineRules
            .Where(x => x.IsEnabled && x.Scope == "monthly_pack")
            .OrderBy(x => x.DueDayOfMonth)
            .FirstOrDefaultAsync(ct);
        if (deadlineRule is null)
        {
            return 0;
        }

        var reminderRules = await _db.ReminderRules
            .Where(x => x.IsEnabled && x.TriggerType == "deadline_approaching")
            .OrderByDescending(x => x.DaysBeforeDue)
            .ToListAsync(ct);
        if (reminderRules.Count == 0)
        {
            return 0;
        }

        var openPacks = await _db.MonthlyPacks
            .Where(x => x.Status != MonthlyPackStatus.Closed.ToStorageValue() && x.Status != MonthlyPackStatus.Complete.ToStorageValue())
            .ToListAsync(ct);

        var notificationsSent = 0;
        foreach (var pack in openPacks)
        {
            var dueDate = BuildUtcDate(pack.Year, pack.Month, deadlineRule.DueDayOfMonth);
            var daysUntilDue = (dueDate.Date - now.Date).TotalDays;
            if (daysUntilDue < 0)
            {
                continue;
            }

            foreach (var rule in reminderRules.Where(x => x.DaysBeforeDue == daysUntilDue))
            {
                notificationsSent += await SendNotificationIfMissingAsync(
                    pack.ClientId,
                    rule.AudienceRole,
                    "monthly_pack.deadline_approaching",
                    "Monthly pack due soon",
                    $"Monthly pack {pack.Year:D4}-{pack.Month:D2} is due on {dueDate:yyyy-MM-dd}.",
                    $"/monthly-packs/{pack.Id}",
                    new { monthlyPackId = pack.Id, reminderRuleId = rule.Id, dueDate },
                    now,
                    ct);
            }
        }

        return notificationsSent;
    }

    private async Task<(int OverdueRequestsMarked, int NotificationsSent)> ProcessOverdueRequestEscalationsAsync(DateTime now, CancellationToken ct)
    {
        var rules = await GetEscalationRulesAsync(ct);
        if (rules.Count == 0)
        {
            return (0, 0);
        }

        var requests = await _db.Requests
            .Where(x => x.DueDateUtc != null && x.DueDateUtc < now && x.Status != RequestStatus.Resolved.ToStorageValue())
            .ToListAsync(ct);

        var markedOverdue = 0;
        var notificationsSent = 0;

        foreach (var request in requests)
        {
            var statusBefore = request.Status;
            if (request.Status != RequestStatus.Overdue.ToStorageValue())
            {
                request.MarkOverdue();
                markedOverdue++;
            }

            var daysPastDue = (int)Math.Floor((now.Date - request.DueDateUtc!.Value.Date).TotalDays);
            foreach (var rule in rules.Where(x => x.IsEnabled && x.DaysAfterDue <= daysPastDue))
            {
                var applies = rule.TriggerType switch
                {
                    "overdue_client_action" => statusBefore == RequestStatus.WaitingOnClient.ToStorageValue(),
                    "overdue_accountant_action" => statusBefore == RequestStatus.WaitingOnAccountant.ToStorageValue(),
                    _ => false
                };

                if (!applies)
                {
                    continue;
                }

                notificationsSent += await SendNotificationIfMissingAsync(
                    request.ClientId,
                    rule.EscalateToRole,
                    "request.escalation",
                    "Overdue request escalation",
                    $"Request '{request.Title}' is overdue and requires attention.",
                    $"/requests/{request.Id}",
                    new { requestId = request.Id, ruleId = rule.Id, rule.TriggerType, rule.Action, request.Status, daysPastDue },
                    now,
                    ct);
            }
        }

        if (markedOverdue > 0)
        {
            await _db.SaveChangesAsync(ct);
        }

        return (markedOverdue, notificationsSent);
    }

    private async Task<(int ComplianceItemsUpdated, int NotificationsSent)> ProcessComplianceDeadlinesAsync(DateTime now, CancellationToken ct)
    {
        var reminderRules = await _db.ReminderRules
            .Where(x => x.IsEnabled && x.TriggerType == "deadline_approaching")
            .OrderByDescending(x => x.DaysBeforeDue)
            .ToListAsync(ct);
        if (reminderRules.Count == 0)
        {
            return (0, 0);
        }

        var items = await _db.ComplianceItems.ToListAsync(ct);
        var updatedCount = 0;
        var notificationsSent = 0;

        foreach (var item in items)
        {
            var updated = UpdateComplianceStatus(item, now);
            if (updated)
            {
                updatedCount++;
            }

            var deadline = item.ExpiryDateUtc ?? item.DueDateUtc;
            if (!deadline.HasValue)
            {
                continue;
            }

            var daysUntilDeadline = (deadline.Value.Date - now.Date).TotalDays;
            if (daysUntilDeadline < 0)
            {
                continue;
            }

            foreach (var rule in reminderRules.Where(x => x.DaysBeforeDue == daysUntilDeadline))
            {
                var recipientIds = await _db.ResolveNotificationRecipientsAsync(item.ClientId, rule.AudienceRole, ct);
                foreach (var recipientId in recipientIds.Distinct())
                {
                    if (await HasRecentComplianceReminderAsync(item.Id, recipientId, rule.TriggerType, deadline.Value, ct))
                    {
                        continue;
                    }

                    var reminder = ComplianceReminder.Create(
                        Guid.NewGuid(),
                        item.Id,
                        item.ClientId,
                        recipientId,
                        rule.TriggerType,
                        deadline.Value);
                    reminder.SetStatus(ComplianceReminderStatus.Sent);
                    _db.ComplianceReminders.Add(reminder);

                    await _db.AddNotificationsAsync(
                        actorUserId: null,
                        actorRole: SystemActorRole,
                        recipientUserIds: [recipientId],
                        clientId: item.ClientId,
                        type: "compliance.deadline_approaching",
                        title: "Compliance deadline approaching",
                        message: $"{item.Name} has a compliance deadline on {deadline.Value:yyyy-MM-dd}.",
                        linkUrl: $"/client/compliance/{item.Id}",
                        metadata: new { complianceItemId = item.Id, reminderRuleId = rule.Id, deadline = deadline.Value, reminder.Id },
                        ct: ct);

                    notificationsSent++;
                }
            }
        }

        if (updatedCount > 0)
        {
            await _db.SaveChangesAsync(ct);
        }

        return (updatedCount, notificationsSent);
    }

    private static bool UpdateComplianceStatus(ComplianceItem item, DateTime now)
    {
        if (item.ExpiryDateUtc is null)
        {
            return false;
        }

        var targetStatus = item.ExpiryDateUtc.Value.Date < now.Date
            ? "expired"
            : (item.ExpiryDateUtc.Value.Date - now.Date).TotalDays <= 30
                ? "expiring_soon"
                : item.Status == "expiring_soon"
                    ? "valid"
                    : item.Status;

        if (targetStatus == item.Status)
        {
            return false;
        }

        item.Update(
            item.Name,
            ComplianceDomainValues.ToComplianceItemStatus(targetStatus),
            item.OwnerUserId,
            ComplianceDomainValues.ToComplianceRiskLevel(item.RiskLevel),
            item.RequiredDocumentCategory,
            item.LinkedDocumentId,
            item.DueDateUtc,
            item.ExpiryDateUtc);
        return true;
    }

    private async Task<int> SendNotificationIfMissingAsync(
        Guid clientId,
        string audienceRole,
        string type,
        string title,
        string message,
        string? linkUrl,
        object metadata,
        DateTime now,
        CancellationToken ct)
    {
        var recipientIds = await _db.ResolveNotificationRecipientsAsync(clientId, audienceRole, ct);
        var recipientsToNotify = new List<Guid>();

        foreach (var recipientId in recipientIds.Distinct())
        {
            var exists = await _db.Notifications.AnyAsync(x =>
                x.UserId == recipientId &&
                x.Type == type &&
                x.LinkUrl == linkUrl &&
                x.CreatedAtUtc >= now.AddHours(-24), ct);

            if (!exists)
            {
                recipientsToNotify.Add(recipientId);
            }
        }

        if (recipientsToNotify.Count == 0)
        {
            return 0;
        }

        return await _db.AddNotificationsAsync(
            actorUserId: null,
            actorRole: SystemActorRole,
            recipientUserIds: recipientsToNotify,
            clientId: clientId,
            type: type,
            title: title,
            message: message,
            linkUrl: linkUrl,
            metadata: metadata,
            ct: ct);
    }

    private async Task<bool> HasRecentComplianceReminderAsync(Guid complianceItemId, Guid recipientUserId, string type, DateTime scheduledForUtc, CancellationToken ct)
    {
        return await _db.ComplianceReminders.AnyAsync(x =>
            x.ComplianceItemId == complianceItemId &&
            x.RecipientUserId == recipientUserId &&
            x.Type == type &&
            x.ScheduledForUtc.Date == scheduledForUtc.Date, ct);
    }

    private async Task<IReadOnlyList<EscalationRuleDto>> GetEscalationRulesAsync(CancellationToken ct)
    {
        var setting = await _db.SystemSettings.FindAsync([EscalationRulesKey], ct);
        if (setting is null || string.IsNullOrWhiteSpace(setting.ValueJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<EscalationRuleDto[]>(setting.ValueJson) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static DateTime BuildUtcDate(int year, int month, int day)
    {
        var safeDay = Math.Min(day, DateTime.DaysInMonth(year, month));
        return new DateTime(year, month, safeDay, 0, 0, 0, DateTimeKind.Utc);
    }
}
