using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Compliance;
using SecureClientPortal.Backend.Application.Contracts;
using SecureClientPortal.Backend.Models;

namespace SecureClientPortal.Backend.Controllers;

[ApiController]
[Route("api/compliance")]
[Authorize(Policy = "ClientOrAccountant")]
public class ComplianceController : ControllerBase
{
    private readonly IComplianceService _service;

    public ComplianceController(IComplianceService service)
    {
        _service = service;
    }

    [HttpGet("categories")]
    public async Task<IActionResult> GetCategories(CancellationToken ct)
    {
        return Ok(await _service.GetCategoriesAsync(ct));
    }

    [HttpPost("categories")]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<IActionResult> CreateCategory([FromBody] CreateComplianceCategoryRequest request, CancellationToken ct)
    {
        return await ExecuteAsync(async () => FromResult(await _service.CreateCategoryAsync(request, User, ct)));
    }

    [HttpPost("categories/seed-defaults")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> SeedDefaultCategories(CancellationToken ct)
    {
        return Ok(await _service.SeedDefaultCategoriesAsync(User, ct));
    }

    [HttpGet("items")]
    public async Task<IActionResult> GetItems([FromQuery] string? clientId = null, CancellationToken ct = default)
    {
        return await ExecuteAsync(async () => FromResult(await _service.GetItemsAsync(User, clientId, ct)));
    }

    [HttpPost("items")]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<IActionResult> CreateItem([FromBody] CreateComplianceItemRequest request, CancellationToken ct)
    {
        return await ExecuteAsync(async () => FromResult(await _service.CreateItemAsync(request, User, ct)));
    }

    [HttpPut("items/{id}")]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<IActionResult> UpdateItem(string id, [FromBody] UpdateComplianceItemRequest request, CancellationToken ct)
    {
        return await ExecuteAsync(async () => FromResult(await _service.UpdateItemAsync(id, request, User, ct)));
    }

    [HttpGet("alerts")]
    public async Task<IActionResult> GetAlerts([FromQuery] string? clientId = null, CancellationToken ct = default)
    {
        return await ExecuteAsync(async () => FromResult(await _service.GetAlertsAsync(User, clientId, ct)));
    }

    [HttpGet("reminders")]
    public async Task<IActionResult> GetReminders([FromQuery] string? clientId = null, CancellationToken ct = default)
    {
        return await ExecuteAsync(async () => FromResult(await _service.GetRemindersAsync(User, clientId, ct)));
    }

    [HttpPost("reminders")]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<IActionResult> CreateReminder([FromBody] CreateComplianceReminderRequest request, CancellationToken ct)
    {
        return await ExecuteAsync(async () => FromResult(await _service.CreateReminderAsync(request, User, ct)));
    }

    [HttpPut("reminders/{id}/status")]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<IActionResult> UpdateReminderStatus(string id, [FromBody] UpdateComplianceReminderStatusRequest request, CancellationToken ct)
    {
        return await ExecuteAsync(async () => FromResult(await _service.UpdateReminderStatusAsync(id, request, User, ct)));
    }

    [HttpGet("reports/summary")]
    public async Task<IActionResult> GetSummaryReport([FromQuery] string? clientId = null, CancellationToken ct = default)
    {
        return await ExecuteAsync(async () => FromResult(await _service.GetSummaryReportAsync(User, clientId, ct)));
    }

    private async Task<IActionResult> ExecuteAsync(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (AppValidationException ex)
        {
            return BadRequest(new { error = ex.Message, errors = ex.Errors });
        }
    }

    private IActionResult FromResult<T>(ServiceResult<T> result)
    {
        if (result.Forbidden) return Forbid();
        if (result.NotFound) return string.IsNullOrWhiteSpace(result.Error) ? NotFound() : NotFound(new { error = result.Error });
        if (result.Unauthorized) return StatusCode(result.StatusCode ?? StatusCodes.Status401Unauthorized, new { code = result.ErrorCode, message = result.Error });
        if (!string.IsNullOrWhiteSpace(result.Error)) return StatusCode(result.StatusCode ?? StatusCodes.Status400BadRequest, new { code = result.ErrorCode, error = result.Error });
        return Ok(result.Value);
    }
}
