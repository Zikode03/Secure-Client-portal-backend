using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureClientPortal.Backend.Application;
using SecureClientPortal.Backend.Application.Contracts;
using SecureClientPortal.Backend.Application.FirmManagement;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Models;
using System.Text.Json;

namespace SecureClientPortal.Backend.Controllers;

[ApiController]
[Route("api/admin/firm-management")]
[Authorize(Policy = "AdminOnly")]
public class FirmManagementController : ControllerBase
{
    private const string EscalationRulesKey = "firm.escalation_rules";
    private readonly IFirmManagementService _firmManagement;
    private readonly ICurrentUserContextFactory _currentUserContextFactory;
    private readonly PortalDbContext _db;

    public FirmManagementController(
        IFirmManagementService firmManagement,
        ICurrentUserContextFactory currentUserContextFactory,
        PortalDbContext db)
    {
        _firmManagement = firmManagement;
        _currentUserContextFactory = currentUserContextFactory;
        _db = db;
    }

    public FirmManagementController(PortalDbContext db)
        : this(new FirmManagementService(db), new CurrentUserContextFactory(), db)
    {
    }

    [HttpGet("templates/required-documents")]
    public async Task<ActionResult<IEnumerable<RequiredDocumentTemplateDto>>> GetRequiredDocumentTemplates()
        => Ok(await _firmManagement.GetRequiredDocumentTemplatesAsync());

    [HttpPut("templates/required-documents")]
    public async Task<IActionResult> PutRequiredDocumentTemplates([FromBody] RequiredDocumentTemplateDto[] templates)
    {
        await _firmManagement.ReplaceRequiredDocumentTemplatesAsync(
            templates,
            _currentUserContextFactory.Create(User, HttpContext));
        return Ok(templates);
    }

    [HttpGet("templates/monthly-pack")]
    public async Task<ActionResult<IEnumerable<MonthlyPackTemplateDto>>> GetMonthlyPackTemplates()
        => Ok(await _firmManagement.GetMonthlyPackTemplatesAsync());

    [HttpPut("templates/monthly-pack")]
    public async Task<IActionResult> PutMonthlyPackTemplates([FromBody] MonthlyPackTemplateDto[] templates)
    {
        await _firmManagement.ReplaceMonthlyPackTemplatesAsync(
            templates,
            _currentUserContextFactory.Create(User, HttpContext));
        return Ok(templates);
    }

    [HttpGet("templates/requests")]
    public async Task<ActionResult<IEnumerable<RequestTemplateDto>>> GetRequestTemplates()
        => Ok(await _firmManagement.GetRequestTemplatesAsync());

    [HttpPut("templates/requests")]
    public async Task<IActionResult> PutRequestTemplates([FromBody] RequestTemplateDto[] templates)
    {
        await _firmManagement.ReplaceRequestTemplatesAsync(
            templates,
            _currentUserContextFactory.Create(User, HttpContext));
        return Ok(templates);
    }

    [HttpGet("rules/reminders")]
    public async Task<ActionResult<IEnumerable<ReminderRuleDto>>> GetReminderRules()
        => Ok(await _firmManagement.GetReminderRulesAsync());

    [HttpPut("rules/reminders")]
    public async Task<IActionResult> PutReminderRules([FromBody] ReminderRuleDto[] rules)
    {
        await _firmManagement.ReplaceReminderRulesAsync(
            rules,
            _currentUserContextFactory.Create(User, HttpContext));
        return Ok(rules);
    }

    [HttpGet("rules/deadlines")]
    public async Task<ActionResult<IEnumerable<DeadlineRuleDto>>> GetDeadlineRules()
        => Ok(await _firmManagement.GetDeadlineRulesAsync());

    [HttpPut("rules/deadlines")]
    public async Task<IActionResult> PutDeadlineRules([FromBody] DeadlineRuleDto[] rules)
    {
        await _firmManagement.ReplaceDeadlineRulesAsync(
            rules,
            _currentUserContextFactory.Create(User, HttpContext));
        return Ok(rules);
    }

    [HttpGet("rules/escalations")]
    public async Task<ActionResult<IEnumerable<EscalationRuleDto>>> GetEscalationRules()
        => Ok(await GetSettingAsync(EscalationRulesKey, DefaultEscalationRules()));

    [HttpPut("rules/escalations")]
    public async Task<IActionResult> PutEscalationRules([FromBody] EscalationRuleDto[] rules)
    {
        var item = await _db.SystemSettings.FindAsync(EscalationRulesKey);
        if (item is null)
        {
            item = new SystemSetting { Key = EscalationRulesKey };
            _db.SystemSettings.Add(item);
        }

        item.ValueJson = JsonSerializer.Serialize(rules);
        item.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _db.WriteAuditLogAsync(User, "firm.escalation_rules_updated", "firm_management", EscalationRulesKey);
        return Ok(rules);
    }

    [HttpPost("seed-defaults")]
    public async Task<IActionResult> SeedDefaults()
    {
        await _firmManagement.SeedDefaultsAsync(_currentUserContextFactory.Create(User, HttpContext));
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

    private static EscalationRuleDto[] DefaultEscalationRules() =>
    [
        new("er_client_overdue", "Client overdue escalation", "overdue_client_action", 2, "accountant", "create_request", true),
        new("er_accountant_overdue", "Accountant overdue escalation", "overdue_accountant_action", 5, "admin", "notify_admin", true)
    ];
}
