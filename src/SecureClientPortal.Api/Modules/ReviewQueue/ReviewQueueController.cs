using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts.Modules.Documents;
using SecureClientPortal.Backend.Application.Contracts.Modules.ReviewQueue;
using SecureClientPortal.Backend.Application.Modules.Documents;
using SecureClientPortal.Backend.Application.Modules.ReviewQueue;
using SecureClientPortal.Backend.Domain.Modules.Documents;

namespace SecureClientPortal.Backend.Api.Modules.ReviewQueue;

[ApiController]
[Route("api/review-queue")]
[Authorize(Policy = "AccountantOnly")]
public class ReviewQueueController : ControllerBase
{
    private readonly IReviewQueueService _reviewQueueService;
    private readonly IDocumentWorkflowService _documentWorkflowService;

    public ReviewQueueController(IReviewQueueService reviewQueueService, IDocumentWorkflowService documentWorkflowService)
    {
        _reviewQueueService = reviewQueueService;
        _documentWorkflowService = documentWorkflowService;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ReviewQueueItemResponse>>> GetPending([FromQuery] ReviewQueueFilterRequest request, CancellationToken ct)
    {
        var result = await _reviewQueueService.GetPendingAsync(User, request, ct);
        return result.forbidden ? Forbid() : Ok(result.items);
    }

    [HttpGet("{documentId}")]
    public async Task<IActionResult> GetWorkspace(string documentId, CancellationToken ct)
    {
        return await ExecuteAsync(async () => FromResult(await _reviewQueueService.GetWorkspaceAsync(documentId, User, HttpContext, ct)));
    }

    [HttpPost("{documentId}/review")]
    public async Task<IActionResult> Review(string documentId, [FromBody] AddReviewDecisionRequest request, CancellationToken ct)
    {
        return await ExecuteAsync(async () => FromResult(await _documentWorkflowService.ReviewAsync(documentId, request, User, ct)));
    }

    [HttpPost("{documentId}/request-reupload")]
    public async Task<IActionResult> RequestReupload(string documentId, [FromBody] RequestReuploadRequest request, CancellationToken ct)
    {
        return await ExecuteAsync(async () => FromResult(await _documentWorkflowService.RequestReuploadAsync(documentId, request, User, ct)));
    }

    [HttpPost("{documentId}/comments")]
    public async Task<IActionResult> AddComment(string documentId, [FromBody] AddDocumentCommentRequest request, CancellationToken ct)
    {
        return await ExecuteAsync(async () => FromResult(await _documentWorkflowService.AddCommentAsync(documentId, request, User, ct)));
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
