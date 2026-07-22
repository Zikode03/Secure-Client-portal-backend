using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts.Modules.Requests;
using SecureClientPortal.Backend.Application.Modules.Requests;
using SecureClientPortal.Backend.Models;

namespace SecureClientPortal.Backend.Api.Modules.Requests;

[ApiController]
[Route("api/requests")]
[Authorize(Policy = "ClientOrAccountant")]
public class RequestsController : ControllerBase
{
    private readonly IRequestService _requests;

    public RequestsController(IRequestService requests)
    {
        _requests = requests;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<RequestItem>>> GetAll(CancellationToken ct)
    {
        var result = await _requests.GetAllAsync(User, ct);
        return result.forbidden ? Forbid() : Ok(result.results);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRequestRequest request, CancellationToken ct)
    {
        return await ExecuteAsync(async () =>
        {
            var result = await _requests.CreateAsync(request, User, ct);
            return result.forbidden ? Forbid() : CreatedAtAction(nameof(GetById), new { id = result.created.Id }, result.created);
        });
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<RequestItem>> GetById(string id, CancellationToken ct)
    {
        var result = await _requests.GetByIdAsync(id, User, ct);
        if (result.forbidden)
        {
            return Forbid();
        }

        return result.item is null ? NotFound() : Ok(result.item);
    }

    [HttpGet("{id}/workspace")]
    public async Task<IActionResult> GetWorkspace(string id, CancellationToken ct)
    {
        return await ExecuteAsync(async () => FromResult(await _requests.GetWorkspaceAsync(id, User, ct)));
    }

    [HttpPut("{id}")]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateRequestRequest request, CancellationToken ct)
    {
        return await ExecuteAsync(async () =>
        {
            var result = await _requests.UpdateAsync(id, request, User, ct);
            if (result.forbidden)
            {
                return Forbid();
            }

            return result.updated is null ? NotFound() : Ok(result.updated);
        });
    }

    [HttpPatch("{id}/status")]
    public async Task<IActionResult> UpdateStatus(string id, [FromBody] UpdateRequestStatusRequest request, CancellationToken ct)
    {
        return await ExecuteAsync(async () =>
        {
            var result = await _requests.UpdateStatusAsync(id, request, User, ct);
            if (result.forbidden)
            {
                return Forbid();
            }

            return result.updated is null ? NotFound() : Ok(result.updated);
        });
    }

    [HttpGet("{id}/comments")]
    public async Task<ActionResult<IEnumerable<RequestComment>>> GetComments(string id, CancellationToken ct)
    {
        var result = await _requests.GetCommentsAsync(id, User, ct);
        if (result.forbidden)
        {
            return Forbid();
        }

        return result.comments is null ? NotFound() : Ok(result.comments);
    }

    [HttpPost("{id}/comments")]
    public async Task<IActionResult> AddComment(string id, [FromBody] AddRequestCommentRequest request, CancellationToken ct)
    {
        return await ExecuteAsync(async () =>
        {
            var result = await _requests.AddCommentAsync(id, request, User, ct);
            if (result.forbidden)
            {
                return Forbid();
            }

            return result.comment is null ? NotFound() : Ok(result.comment);
        });
    }

    [HttpPost("{id}/upload")]
    [RequestSizeLimit(100_000_000)]
    public async Task<IActionResult> UploadDocument(string id, [FromForm] UploadRequestDocumentRequest request, CancellationToken ct)
    {
        return await ExecuteAsync(async () => FromResult(await _requests.UploadDocumentAsync(id, request, User, ct)));
    }

    [HttpPost("{id}/resolve")]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<IActionResult> Resolve(string id, [FromBody] ResolveRequestRequest request, CancellationToken ct)
    {
        return await ExecuteAsync(async () =>
        {
            var result = await _requests.ResolveAsync(id, request, User, ct);
            if (result.forbidden)
            {
                return Forbid();
            }

            return result.resolved is null ? NotFound() : Ok(result.resolved);
        });
    }

    [HttpDelete("{id}")]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var result = await _requests.DeleteAsync(id, User, ct);
        if (result.forbidden)
        {
            return Forbid();
        }

        return result.deleted ? NoContent() : NotFound();
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
