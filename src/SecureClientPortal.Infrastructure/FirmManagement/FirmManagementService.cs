using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application;
using SecureClientPortal.Backend.Application.Contracts;
using SecureClientPortal.Backend.Application.FirmManagement;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;


namespace SecureClientPortal.Backend.Infrastructure.FirmManagement.Application;

public sealed class FirmManagementService : IFirmManagementService
{
    private const string EscalationRulesKey = "firm.escalation_rules";
    private readonly PortalDbContext _db;

    public FirmManagementService(PortalDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<RequiredDocumentTemplateDto>> GetRequiredDocumentTemplatesAsync(CancellationToken ct = default)
    {
        var items = await _db.RequiredDocumentTemplates.OrderBy(x => x.Name).ToListAsync(ct);
        return items.Count == 0 ? DefaultRequiredDocumentTemplates() : items.Select(MapRequiredDocumentTemplateDto).ToArray();
    }

    public async Task<IReadOnlyList<MonthlyPackTemplateDto>> GetMonthlyPackTemplatesAsync(CancellationToken ct = default)
    {
        var templates = await _db.MonthlyPackTemplates.OrderBy(x => x.Name).ToListAsync(ct);
        if (templates.Count == 0)
        {
            return DefaultMonthlyPackTemplates();
        }

        var templateIds = templates.Select(x => x.Id).ToArray();
        var items = await _db.MonthlyPackTemplateItems
            .Where(x => templateIds.Contains(x.MonthlyPackTemplateId))
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.RequiredDocumentTemplateId)
            .ToListAsync(ct);

        return templates.Select(template => new MonthlyPackTemplateDto(
            template.Id,
            template.Name,
            template.Description,
            items.Where(x => x.MonthlyPackTemplateId == template.Id).Select(x => x.RequiredDocumentTemplateId).ToArray(),
            template.AutoCreateDayOfMonth)).ToArray();
    }

    public async Task<IReadOnlyList<RequestTemplateDto>> GetRequestTemplatesAsync(CancellationToken ct = default)
    {
        var items = await _db.RequestTemplates.OrderBy(x => x.Name).ToListAsync(ct);
        return items.Count == 0 ? DefaultRequestTemplates() : items.Select(MapRequestTemplateDto).ToArray();
    }

    public async Task<IReadOnlyList<ReminderRuleDto>> GetReminderRulesAsync(CancellationToken ct = default)
    {
        var items = await _db.ReminderRules.OrderBy(x => x.Name).ToListAsync(ct);
        return items.Count == 0 ? DefaultReminderRules() : items.Select(MapReminderRuleDto).ToArray();
    }

    public async Task<IReadOnlyList<DeadlineRuleDto>> GetDeadlineRulesAsync(CancellationToken ct = default)
    {
        var items = await _db.DeadlineRules.OrderBy(x => x.Name).ToListAsync(ct);
        return items.Count == 0 ? DefaultDeadlineRules() : items.Select(MapDeadlineRuleDto).ToArray();
    }

    public async Task<IReadOnlyList<EscalationRuleDto>> GetEscalationRulesAsync(CancellationToken ct = default)
    {
        var item = await _db.SystemSettings.FindAsync([EscalationRulesKey], ct);
        if (item is null)
        {
            return DefaultEscalationRules();
        }

        try
        {
            return JsonSerializer.Deserialize<EscalationRuleDto[]>(item.ValueJson) ?? DefaultEscalationRules();
        }
        catch
        {
            return DefaultEscalationRules();
        }
    }

    public async Task ReplaceRequiredDocumentTemplatesAsync(IEnumerable<RequiredDocumentTemplateDto> templates, CurrentUserContext actor, CancellationToken ct = default)
    {
        _db.RequiredDocumentTemplates.RemoveRange(_db.RequiredDocumentTemplates);
        foreach (var template in templates)
        {
            _db.RequiredDocumentTemplates.Add(RequiredDocumentTemplate.Create(
                template.Id,
                template.Name,
                template.Description,
                template.DocumentCategory,
                template.IsRequired,
                template.DefaultDueDayOfMonth,
                true));
        }

        await _db.SaveChangesAsync(ct);
        await WriteAuditAsync(actor, "firm.required_document_templates_updated", "required_document_templates", ct);
    }

    public async Task ReplaceMonthlyPackTemplatesAsync(IEnumerable<MonthlyPackTemplateDto> templates, CurrentUserContext actor, CancellationToken ct = default)
    {
        _db.MonthlyPackTemplateItems.RemoveRange(_db.MonthlyPackTemplateItems);
        _db.MonthlyPackTemplates.RemoveRange(_db.MonthlyPackTemplates);

        foreach (var template in templates)
        {
            _db.MonthlyPackTemplates.Add(MonthlyPackTemplate.Create(
                template.Id,
                template.Name,
                template.Description,
                template.AutoCreateDayOfMonth,
                true));

            for (var index = 0; index < template.RequiredDocumentTemplateIds.Length; index++)
            {
                _db.MonthlyPackTemplateItems.Add(MonthlyPackTemplateItem.Create(
                    DeterministicGuid($"mpti:{template.Id}:{index + 1}"),
                    template.Id,
                    template.RequiredDocumentTemplateIds[index],
                    index + 1));
            }
        }

        await _db.SaveChangesAsync(ct);
        await WriteAuditAsync(actor, "firm.monthly_pack_templates_updated", "monthly_pack_templates", ct);
    }

    public async Task ReplaceRequestTemplatesAsync(IEnumerable<RequestTemplateDto> templates, CurrentUserContext actor, CancellationToken ct = default)
    {
        _db.RequestTemplates.RemoveRange(_db.RequestTemplates);
        foreach (var template in templates)
        {
            _db.RequestTemplates.Add(RequestTemplate.Create(
                template.Id,
                template.Name,
                template.RequestType,
                template.TitleTemplate,
                template.DescriptionTemplate,
                template.Priority,
                template.DefaultDueInDays,
                true));
        }

        await _db.SaveChangesAsync(ct);
        await WriteAuditAsync(actor, "firm.request_templates_updated", "request_templates", ct);
    }

    public async Task ReplaceReminderRulesAsync(IEnumerable<ReminderRuleDto> rules, CurrentUserContext actor, CancellationToken ct = default)
    {
        _db.ReminderRules.RemoveRange(_db.ReminderRules);
        foreach (var rule in rules)
        {
            _db.ReminderRules.Add(ReminderRule.Create(
                rule.Id,
                rule.Name,
                rule.TriggerType,
                rule.DaysBeforeDue,
                rule.AudienceRole,
                rule.MessageTemplate,
                rule.IsEnabled));
        }

        await _db.SaveChangesAsync(ct);
        await WriteAuditAsync(actor, "firm.reminder_rules_updated", "reminder_rules", ct);
    }

    public async Task ReplaceDeadlineRulesAsync(IEnumerable<DeadlineRuleDto> rules, CurrentUserContext actor, CancellationToken ct = default)
    {
        _db.DeadlineRules.RemoveRange(_db.DeadlineRules);
        foreach (var rule in rules)
        {
            _db.DeadlineRules.Add(DeadlineRule.Create(
                rule.Id,
                rule.Name,
                rule.Scope,
                rule.DueDayOfMonth,
                rule.GraceDays,
                rule.Priority,
                rule.IsEnabled));
        }

        await _db.SaveChangesAsync(ct);
        await WriteAuditAsync(actor, "firm.deadline_rules_updated", "deadline_rules", ct);
    }

    public async Task ReplaceEscalationRulesAsync(IEnumerable<EscalationRuleDto> rules, CurrentUserContext actor, CancellationToken ct = default)
    {
        var item = await _db.SystemSettings.FindAsync([EscalationRulesKey], ct);
        if (item is null)
        {
            item = SystemSetting.Create(EscalationRulesKey, JsonSerializer.Serialize(rules));
            _db.SystemSettings.Add(item);
        }
        else
        {
            item.UpdateValue(JsonSerializer.Serialize(rules));
        }

        await _db.SaveChangesAsync(ct);
        await WriteAuditAsync(actor, "firm.escalation_rules_updated", EscalationRulesKey, ct);
    }

    public async Task SeedDefaultsAsync(CurrentUserContext actor, CancellationToken ct = default)
    {
        if (!await _db.RequiredDocumentTemplates.AnyAsync(ct))
        {
            await ReplaceRequiredDocumentTemplatesAsync(DefaultRequiredDocumentTemplates(), actor, ct);
        }

        if (!await _db.MonthlyPackTemplates.AnyAsync(ct))
        {
            await ReplaceMonthlyPackTemplatesAsync(DefaultMonthlyPackTemplates(), actor, ct);
        }

        if (!await _db.RequestTemplates.AnyAsync(ct))
        {
            await ReplaceRequestTemplatesAsync(DefaultRequestTemplates(), actor, ct);
        }

        if (!await _db.ReminderRules.AnyAsync(ct))
        {
            await ReplaceReminderRulesAsync(DefaultReminderRules(), actor, ct);
        }

        if (!await _db.DeadlineRules.AnyAsync(ct))
        {
            await ReplaceDeadlineRulesAsync(DefaultDeadlineRules(), actor, ct);
        }

        await EnsureEscalationSettingAsync(ct);
        await _db.WriteAuditLogAsync(actor.UserId, actor.RoleScope, "firm.management_defaults_seeded", "firm_management", DeterministicGuid("firm.management_defaults_seeded"), null, null, ct);
    }

    private async Task EnsureEscalationSettingAsync(CancellationToken ct)
    {
        var existing = await _db.SystemSettings.FindAsync([EscalationRulesKey], ct);
        if (existing is not null)
        {
            return;
        }

        _db.SystemSettings.Add(SystemSetting.Create(
            EscalationRulesKey,
            JsonSerializer.Serialize(DefaultEscalationRules())));
        await _db.SaveChangesAsync(ct);
    }

    private async Task WriteAuditAsync(CurrentUserContext actor, string action, string entityKey, CancellationToken ct)
    {
        await _db.WriteAuditLogAsync(actor.UserId, actor.RoleScope, action, "firm_management", DeterministicGuid(entityKey), null, null, ct);
    }

    private static RequiredDocumentTemplateDto MapRequiredDocumentTemplateDto(RequiredDocumentTemplate item)
        => new(item.Id, item.Name, item.Description, item.DocumentCategory, item.IsRequired, item.DefaultDueDayOfMonth);

    private static RequestTemplateDto MapRequestTemplateDto(RequestTemplate item)
        => new(item.Id, item.Name, item.RequestType, item.TitleTemplate, item.DescriptionTemplate, item.Priority, item.DefaultDueInDays);

    private static ReminderRuleDto MapReminderRuleDto(ReminderRule item)
        => new(item.Id, item.Name, item.TriggerType, item.DaysBeforeDue, item.AudienceRole, item.MessageTemplate, item.IsEnabled);

    private static DeadlineRuleDto MapDeadlineRuleDto(DeadlineRule item)
        => new(item.Id, item.Name, item.Scope, item.DueDayOfMonth, item.GraceDays, item.Priority, item.IsEnabled);

    private static RequiredDocumentTemplateDto[] DefaultRequiredDocumentTemplates() =>
    [
        new(DeterministicGuid("rdt_bank_statement"), "Bank Statement", "Default monthly bank statement requirement.", "bank_statement", true, 5),
        new(DeterministicGuid("rdt_invoices"), "Invoices", "Default monthly invoice support requirement.", "invoices", true, 5),
        new(DeterministicGuid("rdt_signed_docs"), "Signed Documents", "Approvals and signatures where needed.", "signed_documents", false, null)
    ];

    private static MonthlyPackTemplateDto[] DefaultMonthlyPackTemplates() =>
    [
        new(DeterministicGuid("mpt_default"), "Default Monthly Pack", "Standard monthly client collection pack.", [DeterministicGuid("rdt_bank_statement"), DeterministicGuid("rdt_invoices")], 1)
    ];

    private static RequestTemplateDto[] DefaultRequestTemplates() =>
    [
        new(DeterministicGuid("rqt_reupload"), "Re-upload Request", "reupload_required", "Re-upload required: {{documentName}}", "{{reason}}", "high", 2),
        new(DeterministicGuid("rqt_missing"), "Missing Document Request", "missing_document", "Missing document: {{documentName}}", "Please upload the required document.", "medium", 3),
        new(DeterministicGuid("rqt_signature"), "Signature Request", "signature_required", "Signature required: {{documentName}}", "Please review and sign the attached item.", "medium", 5)
    ];

    private static ReminderRuleDto[] DefaultReminderRules() =>
    [
        new(DeterministicGuid("rr_deadline_7"), "7-day reminder", "deadline_approaching", 7, "client", "A compliance deadline is due in 7 days.", true),
        new(DeterministicGuid("rr_deadline_1"), "1-day reminder", "deadline_approaching", 1, "client", "A compliance deadline is due tomorrow.", true)
    ];

    private static DeadlineRuleDto[] DefaultDeadlineRules() =>
    [
        new(DeterministicGuid("dr_monthly_pack"), "Monthly pack due date", "monthly_pack", 5, 2, "high", true),
        new(DeterministicGuid("dr_compliance_item"), "Compliance item due date", "compliance_item", 25, 0, "critical", true)
    ];

    private static Guid DeterministicGuid(string value)
    {
        using var md5 = MD5.Create();
        return new Guid(md5.ComputeHash(Encoding.UTF8.GetBytes($"secure-client-portal:{value}")));
    }

    private static EscalationRuleDto[] DefaultEscalationRules() =>
    [
        new(DeterministicGuid("er_client_overdue"), "Client overdue escalation", "overdue_client_action", 2, "accountant", "create_request", true),
        new(DeterministicGuid("er_accountant_overdue"), "Accountant overdue escalation", "overdue_accountant_action", 5, "admin", "notify_admin", true)
    ];
}




