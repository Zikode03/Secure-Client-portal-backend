using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Models;
using System.Text.Json;

namespace SecureClientPortal.Backend.Controllers;

public record RequiredDocumentTemplateDto(
    string Id,
    string Name,
    string Description,
    string DocumentCategory,
    bool IsRequired,
    int? DefaultDueDayOfMonth);

public record MonthlyPackTemplateDto(
    string Id,
    string Name,
    string Description,
    string[] RequiredDocumentTemplateIds,
    int AutoCreateDayOfMonth);

public record RequestTemplateDto(
    string Id,
    string Name,
    string RequestType,
    string TitleTemplate,
    string DescriptionTemplate,
    string Priority,
    int? DefaultDueInDays);

public record ReminderRuleDto(
    string Id,
    string Name,
    string TriggerType,
    int DaysBeforeDue,
    string AudienceRole,
    string MessageTemplate,
    bool IsEnabled);

public record DeadlineRuleDto(
    string Id,
    string Name,
    string Scope,
    int DueDayOfMonth,
    int GraceDays,
    string Priority,
    bool IsEnabled);

public record EscalationRuleDto(
    string Id,
    string Name,
    string TriggerType,
    int DaysAfterDue,
    string EscalateToRole,
    string Action,
    bool IsEnabled);

[ApiController]
[Route("api/admin/firm-management")]
[Authorize(Policy = "AdminOnly")]
public class FirmManagementController : ControllerBase
{
    private const string RequiredDocumentTemplatesKey = "firm.required_document_templates";
    private const string MonthlyPackTemplatesKey = "firm.monthly_pack_templates";
    private const string RequestTemplatesKey = "firm.request_templates";
    private const string ReminderRulesKey = "firm.reminder_rules";
    private const string DeadlineRulesKey = "firm.deadline_rules";
    private const string EscalationRulesKey = "firm.escalation_rules";

    private readonly PortalDbContext _db;

    public FirmManagementController(PortalDbContext db)
    {
        // Phase 5 keeps firm-level templates and rules in SystemSettings until they justify a dedicated model.
        _db = db;
    }

    [HttpGet("templates/required-documents")]
    public async Task<ActionResult<IEnumerable<RequiredDocumentTemplateDto>>> GetRequiredDocumentTemplates()
        => Ok(await GetSettingAsync(RequiredDocumentTemplatesKey, DefaultRequiredDocumentTemplates()));

    [HttpPut("templates/required-documents")]
    public async Task<IActionResult> PutRequiredDocumentTemplates([FromBody] RequiredDocumentTemplateDto[] templates)
        => await SaveSettingAsync(RequiredDocumentTemplatesKey, templates, "firm.required_document_templates_updated");

    [HttpGet("templates/monthly-pack")]
    public async Task<ActionResult<IEnumerable<MonthlyPackTemplateDto>>> GetMonthlyPackTemplates()
        => Ok(await GetSettingAsync(MonthlyPackTemplatesKey, DefaultMonthlyPackTemplates()));

    [HttpPut("templates/monthly-pack")]
    public async Task<IActionResult> PutMonthlyPackTemplates([FromBody] MonthlyPackTemplateDto[] templates)
        => await SaveSettingAsync(MonthlyPackTemplatesKey, templates, "firm.monthly_pack_templates_updated");

    [HttpGet("templates/requests")]
    public async Task<ActionResult<IEnumerable<RequestTemplateDto>>> GetRequestTemplates()
        => Ok(await GetSettingAsync(RequestTemplatesKey, DefaultRequestTemplates()));

    [HttpPut("templates/requests")]
    public async Task<IActionResult> PutRequestTemplates([FromBody] RequestTemplateDto[] templates)
        => await SaveSettingAsync(RequestTemplatesKey, templates, "firm.request_templates_updated");

    [HttpGet("rules/reminders")]
    public async Task<ActionResult<IEnumerable<ReminderRuleDto>>> GetReminderRules()
        => Ok(await GetSettingAsync(ReminderRulesKey, DefaultReminderRules()));

    [HttpPut("rules/reminders")]
    public async Task<IActionResult> PutReminderRules([FromBody] ReminderRuleDto[] rules)
        => await SaveSettingAsync(ReminderRulesKey, rules, "firm.reminder_rules_updated");

    [HttpGet("rules/deadlines")]
    public async Task<ActionResult<IEnumerable<DeadlineRuleDto>>> GetDeadlineRules()
        => Ok(await GetSettingAsync(DeadlineRulesKey, DefaultDeadlineRules()));

    [HttpPut("rules/deadlines")]
    public async Task<IActionResult> PutDeadlineRules([FromBody] DeadlineRuleDto[] rules)
        => await SaveSettingAsync(DeadlineRulesKey, rules, "firm.deadline_rules_updated");

    [HttpGet("rules/escalations")]
    public async Task<ActionResult<IEnumerable<EscalationRuleDto>>> GetEscalationRules()
        => Ok(await GetSettingAsync(EscalationRulesKey, DefaultEscalationRules()));

    [HttpPut("rules/escalations")]
    public async Task<IActionResult> PutEscalationRules([FromBody] EscalationRuleDto[] rules)
        => await SaveSettingAsync(EscalationRulesKey, rules, "firm.escalation_rules_updated");

    [HttpPost("seed-defaults")]
    public async Task<IActionResult> SeedDefaults()
    {
        await EnsureSettingAsync(RequiredDocumentTemplatesKey, DefaultRequiredDocumentTemplates());
        await EnsureSettingAsync(MonthlyPackTemplatesKey, DefaultMonthlyPackTemplates());
        await EnsureSettingAsync(RequestTemplatesKey, DefaultRequestTemplates());
        await EnsureSettingAsync(ReminderRulesKey, DefaultReminderRules());
        await EnsureSettingAsync(DeadlineRulesKey, DefaultDeadlineRules());
        await EnsureSettingAsync(EscalationRulesKey, DefaultEscalationRules());
        await _db.WriteAuditLogAsync(User, "firm.management_defaults_seeded", "firm_management", "defaults");
        return Ok(new { seeded = true });
    }

    private async Task<T> GetSettingAsync<T>(string key, T fallback)
    {
        var item = await _db.SystemSettings.FindAsync(key);
        if (item is null)
        {
            return fallback;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(item.ValueJson) ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private async Task EnsureSettingAsync<T>(string key, T value)
    {
        var existing = await _db.SystemSettings.FindAsync(key);
        if (existing is not null)
        {
            return;
        }

        _db.SystemSettings.Add(new SystemSetting
        {
            Key = key,
            ValueJson = JsonSerializer.Serialize(value),
            UpdatedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }

    private async Task<IActionResult> SaveSettingAsync<T>(string key, T value, string auditAction)
    {
        var item = await _db.SystemSettings.FindAsync(key);
        if (item is null)
        {
            item = new SystemSetting { Key = key };
            _db.SystemSettings.Add(item);
        }

        item.ValueJson = JsonSerializer.Serialize(value);
        item.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _db.WriteAuditLogAsync(User, auditAction, "firm_management", key);
        return Ok(value);
    }

    private static RequiredDocumentTemplateDto[] DefaultRequiredDocumentTemplates() =>
    [
        new("rdt_bank_statement", "Bank Statement", "Default monthly bank statement requirement.", "bank_statement", true, 5),
        new("rdt_invoices", "Invoices", "Default monthly invoice support requirement.", "invoices", true, 5),
        new("rdt_signed_docs", "Signed Documents", "Approvals and signatures where needed.", "signed_documents", false, null)
    ];

    private static MonthlyPackTemplateDto[] DefaultMonthlyPackTemplates() =>
    [
        new("mpt_default", "Default Monthly Pack", "Standard monthly client collection pack.", ["rdt_bank_statement", "rdt_invoices"], 1)
    ];

    private static RequestTemplateDto[] DefaultRequestTemplates() =>
    [
        new("rqt_reupload", "Re-upload Request", "reupload", "Re-upload required: {{documentName}}", "{{reason}}", "high", 2),
        new("rqt_missing", "Missing Document Request", "missing_document", "Missing document: {{documentName}}", "Please upload the required document.", "medium", 3),
        new("rqt_signature", "Signature Request", "signature", "Signature required: {{documentName}}", "Please review and sign the attached item.", "medium", 5)
    ];

    private static ReminderRuleDto[] DefaultReminderRules() =>
    [
        new("rr_deadline_7", "7-day reminder", "deadline_approaching", 7, "client", "A compliance deadline is due in 7 days.", true),
        new("rr_deadline_1", "1-day reminder", "deadline_approaching", 1, "client", "A compliance deadline is due tomorrow.", true)
    ];

    private static DeadlineRuleDto[] DefaultDeadlineRules() =>
    [
        new("dr_monthly_pack", "Monthly pack due date", "monthly_pack", 5, 2, "high", true),
        new("dr_compliance_item", "Compliance item due date", "compliance_item", 25, 0, "critical", true)
    ];

    private static EscalationRuleDto[] DefaultEscalationRules() =>
    [
        new("er_client_overdue", "Client overdue escalation", "overdue_client_action", 2, "accountant", "create_request", true),
        new("er_accountant_overdue", "Accountant overdue escalation", "overdue_accountant_action", 5, "admin", "notify_admin", true)
    ];
}
