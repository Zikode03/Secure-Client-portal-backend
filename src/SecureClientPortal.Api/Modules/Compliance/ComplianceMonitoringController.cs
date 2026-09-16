using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts.Modules.Compliance;
using SecureClientPortal.Backend.Application.Modules.Compliance;

namespace SecureClientPortal.Backend.Api.Modules.Compliance;

[ApiController]
[Route("api/compliance/monitoring/{clientId:guid}")]
[Authorize(Policy = "ClientOrAccountant")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ComplianceMonitoringController(IComplianceMonitoringService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid clientId, CancellationToken ct) => Result(await service.GetAsync(clientId, User, ct));

    [HttpPut]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<IActionResult> Update(Guid clientId, UpdateComplianceMonitoringRequest request, CancellationToken ct) => Result(await service.UpdateAsync(clientId, request, User, ct));

    [HttpGet("history")]
    public async Task<IActionResult> History(Guid clientId, [FromQuery] string? checkCode, [FromQuery] int page = 1, CancellationToken ct = default) => Result(await service.HistoryAsync(clientId, checkCode, page, User, ct));

    [HttpPost("manual-verifications")]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<IActionResult> RecordManual(Guid clientId, RecordManualVerificationRequest request, CancellationToken ct) => Result(await service.RecordManualAsync(clientId, request, User, ct));

    [HttpPost("authority-verifications/cipc")]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<IActionResult> VerifyCipc(Guid clientId, RunAuthorityVerificationRequest request, CancellationToken ct) => Result(await service.VerifyCipcAsync(clientId, request, User, ct));

    private IActionResult Result<T>(ServiceResult<T> result)
    {
        if (result.Forbidden) return Forbid();
        if (result.NotFound) return NotFound();
        if (result.Error is not null) return StatusCode(result.StatusCode ?? 400, new { error = result.Error });
        return Ok(result.Value);
    }
}
