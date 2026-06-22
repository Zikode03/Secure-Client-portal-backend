using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureClientPortal.Backend.Application.Reporting;

namespace SecureClientPortal.Backend.Controllers;

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
}
