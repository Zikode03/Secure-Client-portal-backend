using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts;
using SecureClientPortal.Backend.Application.Documents;
using SecureClientPortal.Backend.Models;

namespace SecureClientPortal.Backend.Controllers;

[ApiController]
[Route("api/documents")]
[Authorize(Policy = "ClientOrAccountant")]
public class DocumentsController : ControllerBase
{
    private readonly IDocumentWorkflowService _service;

    public DocumentsController(IDocumentWorkflowService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        return Ok(await _service.GetAllAsync(User, ct));
    }

    [HttpGet("filing-register")]
    public async Task<IActionResult> GetFilingRegister([FromQuery] string? clientId = null, CancellationToken ct = default)
    {
        return FromResult(await _service.GetFilingRegisterAsync(User, clientId, ct));
    }

    [HttpGet("filing-rules")]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<IActionResult> GetFilingRules(CancellationToken ct)
    {
        return Ok(await _service.GetFilingRulesAsync(ct));
    }

    [HttpPut("filing-rules/{category}")]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<IActionResult> UpdateFilingRule(string category, [FromBody] FilingRuleUpdateRequest request, CancellationToken ct)
    {
        return await ExecuteAsync(async () => FromResult(await _service.UpdateFilingRuleAsync(category, request, ct)));
    }

    [HttpPost("upload")]
    [RequestSizeLimit(100_000_000)]
    public async Task<IActionResult> Upload([FromForm] UploadDocumentRequest request, CancellationToken ct)
    {
        return await ExecuteAsync(async () =>
        {
            var result = await _service.UploadAsync(request, User, ct);
            if (!IsSuccess(result, out var failure))
            {
                return failure;
            }

            var id = result.Value?.GetType().GetProperty("Id")?.GetValue(result.Value)?.ToString();
            return Created($"/api/documents/{id}", result.Value);
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] Document request, CancellationToken ct)
    {
        return await ExecuteAsync(async () =>
        {
            var result = await _service.CreateAsync(request, User, ct);
            if (!IsSuccess(result, out var failure))
            {
                return failure;
            }

            return CreatedAtAction(nameof(GetById), new { id = result.Value!.Id }, result.Value);
        });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id, CancellationToken ct)
    {
        return await ExecuteAsync(async () => FromResult(await _service.GetByIdAsync(id, User, HttpContext, ct)));
    }

    [HttpGet("{id}/versions")]
    public async Task<IActionResult> GetVersions(string id, CancellationToken ct)
    {
        return await ExecuteAsync(async () => FromResult(await _service.GetVersionsAsync(id, User, HttpContext, ct)));
    }

    [HttpGet("{id}/download")]
    public async Task<IActionResult> Download(string id, CancellationToken ct)
    {
        return await ExecuteAsync(async () =>
        {
            var result = await _service.DownloadAsync(id, User, HttpContext, ct);
            if (!IsSuccess(result, out var failure))
            {
                return failure;
            }

            return File(result.Value!.Content.Stream, result.Value!.Content.ContentType, result.Value!.FileName);
        });
    }

    [HttpPut("{id}")]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<IActionResult> Update(string id, [FromBody] Document request, CancellationToken ct)
    {
        return await ExecuteAsync(async () => FromResult(await _service.UpdateAsync(id, request, User, ct)));
    }

    [HttpPut("{id}/status")]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<IActionResult> UpdateStatus(string id, [FromBody] UpdateDocumentStatusRequest request, CancellationToken ct)
    {
        return await ExecuteAsync(async () => FromResult(await _service.UpdateStatusAsync(id, request, User, ct)));
    }

    [HttpPost("{id}/review")]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<IActionResult> Review(string id, [FromBody] AddReviewDecisionRequest request, CancellationToken ct)
    {
        return await ExecuteAsync(async () => FromResult(await _service.ReviewAsync(id, request, User, ct)));
    }

    [HttpPost("{id}/request-reupload")]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<IActionResult> RequestReupload(string id, [FromBody] RequestReuploadRequest request, CancellationToken ct)
    {
        return await ExecuteAsync(async () => FromResult(await _service.RequestReuploadAsync(id, request, User, ct)));
    }

    [HttpPost("{id}/comments")]
    public async Task<IActionResult> AddComment(string id, [FromBody] AddDocumentCommentRequest request, CancellationToken ct)
    {
        return await ExecuteAsync(async () => FromResult(await _service.AddCommentAsync(id, request, User, ct)));
    }

    [HttpGet("{id}/comments")]
    public async Task<IActionResult> GetComments(string id, CancellationToken ct)
    {
        return await ExecuteAsync(async () => FromResult(await _service.GetCommentsAsync(id, User, ct)));
    }

    [HttpDelete("{id}")]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        return await ExecuteAsync(async () =>
        {
            var result = await _service.DeleteAsync(id, User, ct);
            if (result.NotFound) return NotFound();
            if (result.Forbidden) return Forbid();
            return NoContent();
        });
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

    private bool IsSuccess<T>(ServiceResult<T> result, out IActionResult failure)
    {
        if (result.Forbidden)
        {
            failure = Forbid();
            return false;
        }

        if (result.NotFound)
        {
            failure = string.IsNullOrWhiteSpace(result.Error) ? NotFound() : NotFound(new { error = result.Error });
            return false;
        }

        if (result.Unauthorized)
        {
            failure = StatusCode(result.StatusCode ?? StatusCodes.Status401Unauthorized, new { code = result.ErrorCode, message = result.Error });
            return false;
        }

        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            failure = StatusCode(result.StatusCode ?? StatusCodes.Status400BadRequest, new { code = result.ErrorCode, error = result.Error });
            return false;
        }

        failure = Ok();
        return true;
    }
}

