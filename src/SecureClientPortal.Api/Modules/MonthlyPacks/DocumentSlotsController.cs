using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Application.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Domain.Modules.MonthlyPacks;

namespace SecureClientPortal.Backend.Api.Modules.MonthlyPacks;

[ApiController]
[Route("api/document-slots")]
[Authorize(Policy = "ClientOrAccountant")]
public class DocumentSlotsController : ControllerBase
{
    private readonly IDocumentSlotService _documentSlotService;

    public DocumentSlotsController(IDocumentSlotService documentSlotService)
    {
        _documentSlotService = documentSlotService;
    }

    [HttpGet("{monthlyPackId}")]
    public async Task<ActionResult<IEnumerable<DocumentSlotResponse>>> GetByMonthlyPackId(string monthlyPackId, CancellationToken ct)
    {
        var result = await _documentSlotService.GetByMonthlyPackIdAsync(monthlyPackId, User, ct);
        if (result.forbidden)
        {
            return Forbid();
        }

        if (result.items is null)
        {
            return NotFound();
        }

        return Ok(result.items.Select(Map));
    }

    [HttpPost]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<ActionResult<DocumentSlotResponse>> Create([FromBody] CreateDocumentSlotRequest request, CancellationToken ct)
    {
        try
        {
            var result = await _documentSlotService.CreateAsync(request, User, ct);
            if (result.forbidden)
            {
                return Forbid();
            }

            return Created($"/api/document-slots/{result.created.MonthlyPackId}", Map(result.created));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("{slotId}/submit")]
    public async Task<ActionResult<DocumentSlotResponse>> Submit(string slotId, CancellationToken ct)
    {
        var result = await _documentSlotService.SubmitAsync(slotId, User, ct);
        if (result.forbidden)
        {
            return Forbid();
        }

        if (result.invalid)
        {
            return BadRequest(new { error = result.error });
        }

        if (result.slot is null)
        {
            return NotFound();
        }

        return Ok(Map(result.slot));
    }

    [HttpPost("{slotId}/upload")]
    [RequestSizeLimit(100_000_000)]
    public async Task<IActionResult> Upload(string slotId, [FromForm] UploadDocumentSlotRequest request, CancellationToken ct)
    {
        return await ExecuteAsync(async () => FromResult(await _documentSlotService.UploadAsync(slotId, request, User, ct), Map));
    }

    [HttpGet("{slotId}/versions")]
    public async Task<IActionResult> GetVersions(string slotId, CancellationToken ct)
    {
        return await ExecuteAsync(async () => FromResult(await _documentSlotService.GetVersionsAsync(slotId, User, HttpContext, ct)));
    }

    [HttpGet("{slotId}/workspace")]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<IActionResult> GetWorkspace(string slotId, CancellationToken ct)
    {
        return await ExecuteAsync(async () => FromResult(await _documentSlotService.GetWorkspaceAsync(slotId, User, HttpContext, ct)));
    }

    [HttpPost("{slotId}/start-review")]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<IActionResult> StartReview(string slotId, [FromBody] StartDocumentSlotReviewRequest request, CancellationToken ct)
    {
        return await ExecuteAsync(async () => FromResult(await _documentSlotService.StartReviewAsync(slotId, request, User, ct)));
    }

    [HttpPost("{slotId}/approve")]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<IActionResult> Approve(string slotId, [FromBody] ApproveDocumentSlotRequest request, CancellationToken ct)
    {
        return await ExecuteAsync(async () => FromResult(await _documentSlotService.ApproveAsync(slotId, request, User, ct)));
    }

    [HttpPost("{slotId}/reject")]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<IActionResult> Reject(string slotId, [FromBody] RejectDocumentSlotRequest request, CancellationToken ct)
    {
        return await ExecuteAsync(async () => FromResult(await _documentSlotService.RejectAsync(slotId, request, User, ct)));
    }

    [HttpPost("{slotId}/request-reupload")]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<IActionResult> RequestReupload(string slotId, [FromBody] RequestDocumentSlotReuploadRequest request, CancellationToken ct)
    {
        return await ExecuteAsync(async () => FromResult(await _documentSlotService.RequestReuploadAsync(slotId, request, User, ct)));
    }

    [HttpPost("{slotId}/not-applicable")]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<ActionResult<DocumentSlotResponse>> MarkNotApplicable(string slotId, CancellationToken ct)
    {
        var result = await _documentSlotService.MarkNotApplicableAsync(slotId, User, ct);
        if (result.forbidden)
        {
            return Forbid();
        }

        if (result.invalid)
        {
            return BadRequest(new { error = result.error });
        }

        if (result.slot is null)
        {
            return NotFound();
        }

        return Ok(Map(result.slot));
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

    private IActionResult FromResult<TValue, TResponse>(ServiceResult<TValue> result, Func<TValue, TResponse> map)
    {
        if (result.Forbidden) return Forbid();
        if (result.NotFound) return string.IsNullOrWhiteSpace(result.Error) ? NotFound() : NotFound(new { error = result.Error });
        if (result.Unauthorized) return StatusCode(result.StatusCode ?? StatusCodes.Status401Unauthorized, new { code = result.ErrorCode, message = result.Error });
        if (!string.IsNullOrWhiteSpace(result.Error)) return StatusCode(result.StatusCode ?? StatusCodes.Status400BadRequest, new { code = result.ErrorCode, error = result.Error });
        return Ok(map(result.Value!));
    }

    private static DocumentSlotResponse Map(DocumentSlot slot) =>
        new(
            slot.Id,
            slot.MonthlyPackId,
            slot.ClientId,
            slot.Category,
            slot.Label,
            slot.IsRequired,
            slot.Status,
            slot.CanCurrentlyBeSubmitted,
            slot.CurrentDocumentId,
            slot.DueDateUtc,
            slot.SubmittedAtUtc,
            slot.SubmittedByUserId,
            slot.ReviewStatus,
            slot.RejectionReason,
            slot.CreatedAtUtc,
            slot.UpdatedAtUtc);
}
