using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureClientPortal.Backend.Application.Contracts;
using SecureClientPortal.Backend.Application.Requests;
using SecureClientPortal.Backend.Models;

namespace SecureClientPortal.Backend.Controllers;

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
    public async Task<ActionResult<RequestItem>> Create([FromBody] CreateRequestRequest request, CancellationToken ct)
    {
        try
        {
            var result = await _requests.CreateAsync(request, User, ct);
            return result.forbidden ? Forbid() : CreatedAtAction(nameof(GetById), new { id = result.created.Id }, result.created);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
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

    [HttpPut("{id}")]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<ActionResult<RequestItem>> Update(string id, [FromBody] RequestItem request, CancellationToken ct)
    {
        try
        {
            var result = await _requests.UpdateAsync(id, request, User, ct);
            if (result.forbidden)
            {
                return Forbid();
            }

            return result.updated is null ? NotFound() : Ok(result.updated);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPatch("{id}/status")]
    public async Task<ActionResult<RequestItem>> UpdateStatus(string id, [FromBody] UpdateRequestStatusRequest request, CancellationToken ct)
    {
        try
        {
            var result = await _requests.UpdateStatusAsync(id, request, User, ct);
            if (result.forbidden)
            {
                return Forbid();
            }

            return result.updated is null ? NotFound() : Ok(result.updated);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
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
    public async Task<ActionResult<RequestComment>> AddComment(string id, [FromBody] AddRequestCommentRequest request, CancellationToken ct)
    {
        try
        {
            var result = await _requests.AddCommentAsync(id, request, User, ct);
            if (result.forbidden)
            {
                return Forbid();
            }

            return result.comment is null ? NotFound() : Ok(result.comment);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("{id}/resolve")]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<ActionResult<RequestItem>> Resolve(string id, [FromBody] ResolveRequestRequest request, CancellationToken ct)
    {
        var result = await _requests.ResolveAsync(id, request, User, ct);
        if (result.forbidden)
        {
            return Forbid();
        }

        return result.resolved is null ? NotFound() : Ok(result.resolved);
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
}
