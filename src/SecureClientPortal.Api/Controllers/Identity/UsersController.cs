using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts;
using SecureClientPortal.Backend.Application.Identity;

namespace SecureClientPortal.Backend.Controllers;

[ApiController]
[Route("api/users")]
[Authorize(Policy = "AdminOnly")]
public class UsersController : ControllerBase
{
    private readonly IUserService _service;

    public UsersController(IUserService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        return Ok(await _service.GetAllAsync(ct));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest request, CancellationToken ct)
    {
        return await ExecuteAsync(async () =>
        {
            var result = await _service.CreateAsync(request, User, ct);
            if (!IsSuccess(result, out var failure))
            {
                return failure;
            }

            var id = result.Value?.GetType().GetProperty("Id")?.GetValue(result.Value)?.ToString();
            return Created($"/api/users/{id}", result.Value);
        });
    }

    [HttpPost("{id}/activate")]
    public async Task<IActionResult> Activate(string id, CancellationToken ct)
    {
        return await ExecuteAsync(async () => FromResult(await _service.UpdateActivationAsync(id, new UpdateUserActivationRequest(true, null), User, ct)));
    }

    [HttpPost("{id}/deactivate")]
    public async Task<IActionResult> Deactivate(string id, [FromBody] UpdateUserActivationRequest? request = null, CancellationToken ct = default)
    {
        var payload = new UpdateUserActivationRequest(false, request?.Reason);
        return await ExecuteAsync(async () => FromResult(await _service.UpdateActivationAsync(id, payload, User, ct)));
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
