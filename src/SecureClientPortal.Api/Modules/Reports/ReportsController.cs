using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts.Modules.Reports;
using SecureClientPortal.Backend.Application.Modules.Reports;

namespace SecureClientPortal.Backend.Api.Modules.Reports;

[ApiController]
[Route("api/reports")]
[Authorize(Policy = "ClientOrAccountant")]
public class ReportsController : ControllerBase
{
    private readonly IReportService _reportService;

    public ReportsController(IReportService reportService)
    {
        _reportService = reportService;
    }

    [HttpGet("firm")]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<IActionResult> GetFirmReports(CancellationToken ct)
    {
        var result = await _reportService.GetFirmReportsAsync(User, ct);
        if (result.forbidden)
        {
            return Forbid();
        }

        return Ok(result.report);
    }

    [HttpGet("operations")]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<IActionResult> GetOperationsDashboard(CancellationToken ct)
    {
        var result = await _reportService.GetOperationsDashboardAsync(User, ct);
        if (result.forbidden)
        {
            return Forbid();
        }

        return Ok(result.report);
    }

    [HttpGet("accountants")]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<IActionResult> GetAccountantReports(CancellationToken ct)
    {
        return Ok(await _reportService.GetAccountantReportsAsync(ct));
    }

    [HttpGet("clients")]
    public async Task<IActionResult> GetClientReports(CancellationToken ct)
    {
        return Ok(await _reportService.GetClientReportsAsync(User, ct));
    }

    [HttpGet("compliance.pdf")]
    public async Task<IActionResult> DownloadCompliancePdf([FromQuery] string? clientId = null, CancellationToken ct = default)
    {
        var result = await _reportService.GenerateCompliancePdfAsync(User, clientId, ct);
        if (!TryGetValue(result, out var failure)) return failure!;
        return File(result.Value!.Content, result.Value.ContentType, result.Value.FileName);
    }

    [HttpGet("schedules")]
    public async Task<IActionResult> GetSchedules([FromQuery] string? clientId = null, CancellationToken ct = default)
    {
        return FromResult(await _reportService.GetSchedulesAsync(User, clientId, ct));
    }

    [HttpPost("schedules")]
    public async Task<IActionResult> CreateSchedule([FromBody] CreateReportScheduleRequest request, CancellationToken ct)
    {
        var result = await _reportService.CreateScheduleAsync(request, User, ct);
        if (!TryGetValue(result, out var failure)) return failure!;
        return Created($"/api/reports/schedules/{result.Value!.Id}", result.Value);
    }

    [HttpPut("schedules/{id:guid}")]
    public async Task<IActionResult> UpdateSchedule(Guid id, [FromBody] UpdateReportScheduleRequest request, CancellationToken ct)
    {
        return FromResult(await _reportService.UpdateScheduleAsync(id.ToString(), request, User, ct));
    }

    [HttpDelete("schedules/{id:guid}")]
    public async Task<IActionResult> DeleteSchedule(Guid id, CancellationToken ct)
    {
        var result = await _reportService.DeleteScheduleAsync(id.ToString(), User, ct);
        if (!TryGetValue(result, out var failure)) return failure!;
        return NoContent();
    }

    private IActionResult FromResult<T>(ServiceResult<T> result)
    {
        return TryGetValue(result, out var failure) ? Ok(result.Value) : failure!;
    }

    private bool TryGetValue<T>(ServiceResult<T> result, out IActionResult? failure)
    {
        if (result.Forbidden)
        {
            failure = Forbid();
            return false;
        }

        if (result.NotFound)
        {
            failure = NotFound(new { code = result.ErrorCode, message = result.Error ?? "Resource was not found." });
            return false;
        }

        if (result.Unauthorized)
        {
            failure = StatusCode(result.StatusCode ?? StatusCodes.Status401Unauthorized, new { code = result.ErrorCode, message = result.Error });
            return false;
        }

        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            failure = StatusCode(result.StatusCode ?? StatusCodes.Status400BadRequest, new { code = result.ErrorCode, message = result.Error });
            return false;
        }

        failure = null;
        return true;
    }
}
