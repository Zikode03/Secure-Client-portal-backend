using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureClientPortal.Backend.Application;
using SecureClientPortal.Backend.Application.Contracts.Modules.FirmManagement;
using SecureClientPortal.Backend.Application.Modules.FirmManagement;

namespace SecureClientPortal.Backend.Api.Modules.FirmManagement;

[ApiController]
[Route("api/admin/firm-management")]
[Authorize(Policy = "AdminOnly")]
public class FirmManagementController : ControllerBase
{
    private readonly IFirmManagementService _firmManagement;
    private readonly ICurrentUserContextFactory _currentUserContextFactory;

    public FirmManagementController(
        IFirmManagementService firmManagement,
        ICurrentUserContextFactory currentUserContextFactory)
    {
        _firmManagement = firmManagement;
        _currentUserContextFactory = currentUserContextFactory;
    }

    [HttpGet("templates/required-documents")]
    [SecureClientPortal.Backend.Api.Security.RequirePermission("settings.read")]
    public async Task<ActionResult<IEnumerable<RequiredDocumentTemplateDto>>> GetRequiredDocumentTemplates()
        => Ok(await _firmManagement.GetRequiredDocumentTemplatesAsync());

    [HttpPut("templates/required-documents")]
    [SecureClientPortal.Backend.Api.Security.RequirePermission("templates.update")]
    public async Task<IActionResult> PutRequiredDocumentTemplates([FromBody] RequiredDocumentTemplateDto[] templates)
    {
        await _firmManagement.ReplaceRequiredDocumentTemplatesAsync(
            templates,
            _currentUserContextFactory.Create(User, HttpContext));
        return Ok(templates);
    }

    [HttpGet("templates/monthly-pack")]
    [SecureClientPortal.Backend.Api.Security.RequirePermission("settings.read")]
    public async Task<ActionResult<IEnumerable<MonthlyPackTemplateDto>>> GetMonthlyPackTemplates()
        => Ok(await _firmManagement.GetMonthlyPackTemplatesAsync());

    [HttpPut("templates/monthly-pack")]
    [SecureClientPortal.Backend.Api.Security.RequirePermission("templates.update")]
    public async Task<IActionResult> PutMonthlyPackTemplates([FromBody] MonthlyPackTemplateDto[] templates)
    {
        await _firmManagement.ReplaceMonthlyPackTemplatesAsync(
            templates,
            _currentUserContextFactory.Create(User, HttpContext));
        return Ok(templates);
    }

    [HttpGet("templates/requests")]
    [SecureClientPortal.Backend.Api.Security.RequirePermission("settings.read")]
    public async Task<ActionResult<IEnumerable<RequestTemplateDto>>> GetRequestTemplates()
        => Ok(await _firmManagement.GetRequestTemplatesAsync());

    [HttpPut("templates/requests")]
    [SecureClientPortal.Backend.Api.Security.RequirePermission("templates.update")]
    public async Task<IActionResult> PutRequestTemplates([FromBody] RequestTemplateDto[] templates)
    {
        await _firmManagement.ReplaceRequestTemplatesAsync(
            templates,
            _currentUserContextFactory.Create(User, HttpContext));
        return Ok(templates);
    }

    [HttpGet("rules/reminders")]
    [SecureClientPortal.Backend.Api.Security.RequirePermission("settings.read")]
    public async Task<ActionResult<IEnumerable<ReminderRuleDto>>> GetReminderRules()
        => Ok(await _firmManagement.GetReminderRulesAsync());

    [HttpPut("rules/reminders")]
    [SecureClientPortal.Backend.Api.Security.RequirePermission("settings.update")]
    public async Task<IActionResult> PutReminderRules([FromBody] ReminderRuleDto[] rules)
    {
        await _firmManagement.ReplaceReminderRulesAsync(
            rules,
            _currentUserContextFactory.Create(User, HttpContext));
        return Ok(rules);
    }

    [HttpGet("rules/deadlines")]
    [SecureClientPortal.Backend.Api.Security.RequirePermission("settings.read")]
    public async Task<ActionResult<IEnumerable<DeadlineRuleDto>>> GetDeadlineRules()
        => Ok(await _firmManagement.GetDeadlineRulesAsync());

    [HttpPut("rules/deadlines")]
    [SecureClientPortal.Backend.Api.Security.RequirePermission("deadlines.update")]
    public async Task<IActionResult> PutDeadlineRules([FromBody] DeadlineRuleDto[] rules)
    {
        await _firmManagement.ReplaceDeadlineRulesAsync(
            rules,
            _currentUserContextFactory.Create(User, HttpContext));
        return Ok(rules);
    }

    [HttpGet("rules/escalations")]
    [SecureClientPortal.Backend.Api.Security.RequirePermission("settings.read")]
    public async Task<ActionResult<IEnumerable<EscalationRuleDto>>> GetEscalationRules()
        => Ok(await _firmManagement.GetEscalationRulesAsync());

    [HttpPut("rules/escalations")]
    [SecureClientPortal.Backend.Api.Security.RequirePermission("settings.update")]
    public async Task<IActionResult> PutEscalationRules([FromBody] EscalationRuleDto[] rules)
    {
        await _firmManagement.ReplaceEscalationRulesAsync(
            rules,
            _currentUserContextFactory.Create(User, HttpContext));
        return Ok(rules);
    }

    [HttpPost("seed-defaults")]
    [SecureClientPortal.Backend.Api.Security.RequirePermission("templates.update")]
    [SecureClientPortal.Backend.Api.Security.RequirePermission("deadlines.update")]
    [SecureClientPortal.Backend.Api.Security.RequirePermission("settings.update")]
    public async Task<IActionResult> SeedDefaults()
    {
        await _firmManagement.SeedDefaultsAsync(_currentUserContextFactory.Create(User, HttpContext));
        return Ok(new { seeded = true });
    }
}
