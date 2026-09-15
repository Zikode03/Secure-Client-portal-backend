using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts.Modules.Compliance;
using SecureClientPortal.Backend.Application.Modules.Compliance;

namespace SecureClientPortal.Backend.Api.Modules.Compliance;

[ApiController]
[Route("api/compliance/automation")]
[Authorize(Policy = "ClientOrAccountant")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ComplianceAutomationController(IComplianceAutomationService service) : ControllerBase
{
    [HttpGet("rules")]
    public async Task<IActionResult> Rules(CancellationToken ct) => Result(await service.GetRulesAsync(User, ct));
    [HttpPut("rules"), Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Rules(UpdateComplianceRulesRequest request, CancellationToken ct) => Result(await service.UpdateRulesAsync(request, User, ct));
    [HttpGet("profiles/{clientId:guid}")]
    public async Task<IActionResult> Profile(Guid clientId, CancellationToken ct) => Result(await service.GetProfileAsync(clientId, User, ct));
    [HttpPut("profiles/{clientId:guid}"), Authorize(Policy = "AccountantOnly")]
    public async Task<IActionResult> Profile(Guid clientId, ClientComplianceProfile request, CancellationToken ct) => Result(await service.UpdateProfileAsync(clientId, request, User, ct));
    [HttpGet("obligations")]
    public async Task<IActionResult> Obligations([FromQuery] Guid? clientId, CancellationToken ct) => Result(await service.GetObligationsAsync(clientId, User, ct));
    [HttpPost("run"), Authorize(Policy = "AccountantOnly")]
    public async Task<IActionResult> Run([FromQuery] Guid? clientId, CancellationToken ct) => Result(await service.RunAsync(clientId, User, ct));
    [HttpPost("obligations/{id:guid}/preparation"), Authorize(Policy = "AccountantOnly")]
    public async Task<IActionResult> Preparation(Guid id, PreparationRequest request, CancellationToken ct) => Result(await service.PreparationAsync(id, request, User, ct));
    [HttpPost("obligations/{id:guid}/review"), Authorize(Policy = "AccountantOnly")]
    public async Task<IActionResult> Review(Guid id, ReviewObligationRequest request, CancellationToken ct) => Result(await service.ReviewAsync(id, request, User, ct));
    [HttpPost("obligations/{id:guid}/submission"), Authorize(Policy = "AccountantOnly")]
    public async Task<IActionResult> Submission(Guid id, SubmissionRequest request, CancellationToken ct) => Result(await service.SubmissionAsync(id, request, User, ct));
    [HttpPost("obligations/{id:guid}/payment"), Authorize(Policy = "AccountantOnly")]
    public async Task<IActionResult> Payment(Guid id, PaymentRequest request, CancellationToken ct) => Result(await service.PaymentAsync(id, request, User, ct));
    [HttpPost("obligations/{id:guid}/not-applicable"), Authorize(Policy = "AccountantOnly")]
    public async Task<IActionResult> NotApplicable(Guid id, NotApplicableRequest request, CancellationToken ct) => Result(await service.NotApplicableAsync(id, request, User, ct));
    [HttpGet("obligations/{id:guid}/evidence")]
    public async Task<IActionResult> Evidence(Guid id, CancellationToken ct) => Result(await service.GetEvidenceAsync(id, User, ct));
    [HttpPost("obligations/{id:guid}/evidence"), RequestSizeLimit(100_000_000)]
    public async Task<IActionResult> Evidence(Guid id, [FromForm] UploadComplianceEvidenceRequest request, CancellationToken ct) => Result(await service.UploadEvidenceAsync(id, request, User, ct));

    private IActionResult Result<T>(ServiceResult<T> result)
    {
        if (result.Unauthorized) return Unauthorized();
        if (result.Forbidden) return Forbid();
        if (result.NotFound) return NotFound();
        if (result.Error is not null) return StatusCode(result.StatusCode ?? 400, new { error = result.Error });
        return Ok(result.Value);
    }
}
