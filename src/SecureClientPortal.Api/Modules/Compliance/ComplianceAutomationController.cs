using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts.Modules.Compliance;
using SecureClientPortal.Backend.Application.Modules.Compliance;

namespace SecureClientPortal.Backend.Api.Modules.Compliance;

/// <summary>
/// Obligation-centric compliance workflow. This API automates preparation/readiness and records the
/// outcome of manual external filing; it does not claim to submit returns to SARS/CIPC/UIF.
/// </summary>
[ApiController]
[Route("api/compliance/automation")]
[Authorize(Policy = "ClientOrAccountant")]
public sealed class ComplianceAutomationController(IComplianceAutomationService service) : ControllerBase
{
    [HttpGet("rules")]
    public Task<IActionResult> GetRules(CancellationToken ct) =>
        ExecuteAsync(() => service.GetRulesAsync(User, ct));

    [HttpPut("rules")]
    [Authorize(Policy = "AdminOnly")]
    public Task<IActionResult> UpdateRules([FromBody] UpdateComplianceRuleSetRequest request, CancellationToken ct) =>
        ExecuteAsync(() => service.UpdateRulesAsync(request, User, ct));

    [HttpGet("profiles/{clientId:guid}")]
    public Task<IActionResult> GetProfile(Guid clientId, CancellationToken ct) =>
        ExecuteAsync(() => service.GetProfileAsync(clientId, User, ct));

    [HttpPut("profiles/{clientId:guid}")]
    [Authorize(Policy = "AccountantOnly")]
    public Task<IActionResult> UpdateProfile(Guid clientId, [FromBody] UpdateClientComplianceProfileRequest request, CancellationToken ct) =>
        ExecuteAsync(() => service.UpdateProfileAsync(clientId, request, User, ct));

    [HttpGet("obligations")]
    public Task<IActionResult> GetObligations([FromQuery] Guid? clientId = null, CancellationToken ct = default) =>
        ExecuteAsync(() => service.GetObligationsAsync(User, clientId, ct));

    [HttpPost("run")]
    [Authorize(Policy = "AccountantOnly")]
    public Task<IActionResult> Run([FromQuery] Guid? clientId = null, CancellationToken ct = default) =>
        ExecuteAsync(() => service.RunAsync(User, clientId, null, ct));

    [HttpPost("obligations/{id:guid}/preparation")]
    [Authorize(Policy = "AccountantOnly")]
    public Task<IActionResult> RecordPreparation(Guid id, [FromBody] RecordCompliancePreparationRequest request, CancellationToken ct) =>
        ExecuteAsync(() => service.RecordPreparationAsync(id, request, User, ct));

    [HttpPost("obligations/{id:guid}/review")]
    [Authorize(Policy = "AccountantOnly")]
    public Task<IActionResult> RecordReview(Guid id, [FromBody] RecordComplianceReviewRequest request, CancellationToken ct) =>
        ExecuteAsync(() => service.RecordReviewAsync(id, request, User, ct));

    [HttpPost("obligations/{id:guid}/submission")]
    [Authorize(Policy = "AccountantOnly")]
    public Task<IActionResult> RecordSubmission(Guid id, [FromBody] RecordComplianceSubmissionRequest request, CancellationToken ct) =>
        ExecuteAsync(() => service.RecordSubmissionAsync(id, request, User, ct));

    [HttpPost("obligations/{id:guid}/payment")]
    [Authorize(Policy = "AccountantOnly")]
    public Task<IActionResult> RecordPayment(Guid id, [FromBody] RecordCompliancePaymentRequest request, CancellationToken ct) =>
        ExecuteAsync(() => service.RecordPaymentAsync(id, request, User, ct));

    [HttpPost("obligations/{id:guid}/not-applicable")]
    [Authorize(Policy = "AccountantOnly")]
    public Task<IActionResult> MarkNotApplicable(Guid id, [FromBody] MarkComplianceNotApplicableRequest request, CancellationToken ct) =>
        ExecuteAsync(() => service.MarkNotApplicableAsync(id, request, User, ct));

    private async Task<IActionResult> ExecuteAsync<T>(Func<Task<ServiceResult<T>>> action)
    {
        try
        {
            return FromResult(await action());
        }
        catch (AppValidationException ex)
        {
            return BadRequest(new { error = ex.Message, errors = ex.Errors });
        }
    }

    private IActionResult FromResult<T>(ServiceResult<T> result)
    {
        if (result.Forbidden) return Forbid();
        if (result.NotFound) return NotFound(new { error = result.Error ?? "Not found." });
        if (result.Unauthorized) return StatusCode(result.StatusCode ?? StatusCodes.Status401Unauthorized, new { code = result.ErrorCode, message = result.Error });
        if (!string.IsNullOrWhiteSpace(result.Error)) return StatusCode(result.StatusCode ?? StatusCodes.Status400BadRequest, new { code = result.ErrorCode, error = result.Error });
        return Ok(result.Value);
    }
}
