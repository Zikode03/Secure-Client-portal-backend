using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureClientPortal.Backend.Application;
using SecureClientPortal.Backend.Application.Contracts;
using SecureClientPortal.Backend.Application.FirmManagement;

namespace SecureClientPortal.Backend.Controllers;

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
        => Ok(await _firmManagement.GetEscalationRulesAsync());

    [HttpPut("rules/escalations")]
    public async Task<IActionResult> PutEscalationRules([FromBody] EscalationRuleDto[] rules)
    {
        await _firmManagement.ReplaceEscalationRulesAsync(
            rules,
            _currentUserContextFactory.Create(User, HttpContext));
        return Ok(rules);
    }

    [HttpPost("seed-defaults")]
    public async Task<IActionResult> SeedDefaults()
    {
        await _firmManagement.SeedDefaultsAsync(_currentUserContextFactory.Create(User, HttpContext));
        return Ok(new { seeded = true });
    }
}
