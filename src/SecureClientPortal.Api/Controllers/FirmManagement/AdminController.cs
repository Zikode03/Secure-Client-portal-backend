using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts;
using SecureClientPortal.Backend.Application.Identity;

namespace SecureClientPortal.Backend.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Policy = "AdminOnly")]
public class AdminController : ControllerBase
{
    private readonly IAdminService _service;

    public AdminController(IAdminService service)
    {
        _service = service;
    }

    [HttpGet("users")]
    public async Task<IActionResult> GetUsers(CancellationToken ct)
    {
        return Ok(await _service.GetUsersAsync(ct));
    }

    [HttpPost("users")]
    public async Task<IActionResult> CreateUser([FromBody] AdminCreateUserRequest request, CancellationToken ct)
    {
        return await ExecuteAsync(async () => FromResult(await _service.CreateUserAsync(request, User, ct)));
    }

    [HttpPut("users/{id}/role")]
    public async Task<IActionResult> UpdateUserRole(string id, [FromBody] AdminUpdateRoleRequest request, CancellationToken ct)
    {
        return await ExecuteAsync(async () => FromResult(await _service.UpdateUserRoleAsync(id, request, User, ct)));
    }

    [HttpPut("users/{id}/status")]
    public async Task<IActionResult> UpdateUserStatus(string id, [FromBody] AdminUpdateStatusRequest request, CancellationToken ct)
    {
        return await ExecuteAsync(async () => FromResult(await _service.UpdateUserStatusAsync(id, request, User, ct)));
    }

    [HttpPost("users/{id}/disable")]
    public async Task<IActionResult> DisableUser(string id, CancellationToken ct)
    {
        return await ExecuteAsync(async () => FromResult(await _service.UpdateUserStatusAsync(id, new AdminUpdateStatusRequest("disabled"), User, ct)));
    }

    [HttpPost("users/{id}/enable")]
    public async Task<IActionResult> EnableUser(string id, CancellationToken ct)
    {
        return await ExecuteAsync(async () => FromResult(await _service.UpdateUserStatusAsync(id, new AdminUpdateStatusRequest("active"), User, ct)));
    }

    [HttpPost("users/{id}/reset-access")]
    public async Task<IActionResult> ResetUserAccess(string id, [FromBody] AdminResetAccessRequest request, CancellationToken ct)
    {
        return await ExecuteAsync(async () => FromResult(await _service.ResetUserAccessAsync(id, request, User, ct)));
    }

    [HttpPost("users/{id}/reset-password")]
    public async Task<IActionResult> ResetPassword(string id, [FromBody] AdminResetPasswordRequest request, CancellationToken ct)
    {
        return await ExecuteAsync(async () => FromResult(await _service.ResetPasswordAsync(id, request, User, ct)));
    }

    [HttpGet("settings/{key}")]
    public async Task<IActionResult> GetSetting(string key, CancellationToken ct)
    {
        return Ok(await _service.GetSettingAsync(key, ct));
    }

    [HttpPut("settings/{key}")]
    public async Task<IActionResult> PutSetting(string key, [FromBody] AdminSettingRequest request, CancellationToken ct)
    {
        return await ExecuteAsync(async () => Ok(await _service.PutSettingAsync(key, request, ct)));
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
