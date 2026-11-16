using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts.Modules.UsersRoles;
using SecureClientPortal.Backend.Application.Modules.UsersRoles;

namespace SecureClientPortal.Backend.Api.Modules.UsersRoles;

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
    [SecureClientPortal.Backend.Api.Security.RequirePermission("users.read")]
    public async Task<IActionResult> GetUsers(CancellationToken ct)
    {
        return Ok(await _service.GetUsersAsync(ct));
    }

    [HttpPost("users")]
    [SecureClientPortal.Backend.Api.Security.RequirePermission("users.create")]
    public async Task<IActionResult> CreateUser([FromBody] AdminCreateUserRequest request, CancellationToken ct)
    {
        return await ExecuteAsync(async () => FromResult(await _service.CreateUserAsync(request, User, ct)));
    }

    [HttpPost("users/{id}/resend-invitation")]
    [SecureClientPortal.Backend.Api.Security.RequirePermission("users.create")]
    public Task<IActionResult> ResendInvitation(string id, CancellationToken ct) =>
        ExecuteAsync(async () => FromResult(await _service.ResendInvitationAsync(id, User, ct)));

    [HttpPost("users/{id}/revoke-sessions")]
    [SecureClientPortal.Backend.Api.Security.RequirePermission("users.update")]
    public Task<IActionResult> RevokeSessions(string id, CancellationToken ct) =>
        ExecuteAsync(async () => FromResult(await _service.RevokeSessionsAsync(id, User, ct)));

    [HttpPut("users/{id}/role")]
    [SecureClientPortal.Backend.Api.Security.RequirePermission("users.update")]
    public async Task<IActionResult> UpdateUserRole(string id, [FromBody] AdminUpdateRoleRequest request, CancellationToken ct)
    {
        return await ExecuteAsync(async () => FromResult(await _service.UpdateUserRoleAsync(id, request, User, ct)));
    }

    [HttpPut("users/{id}/status")]
    [SecureClientPortal.Backend.Api.Security.RequirePermission("users.update")]
    public async Task<IActionResult> UpdateUserStatus(string id, [FromBody] AdminUpdateStatusRequest request, CancellationToken ct)
    {
        return await ExecuteAsync(async () => FromResult(await _service.UpdateUserStatusAsync(id, request, User, ct)));
    }

    [HttpPost("users/{id}/disable")]
    [SecureClientPortal.Backend.Api.Security.RequirePermission("users.deactivate")]
    public async Task<IActionResult> DisableUser(string id, CancellationToken ct)
    {
        return await ExecuteAsync(async () => FromResult(await _service.UpdateUserStatusAsync(id, new AdminUpdateStatusRequest("disabled"), User, ct)));
    }

    [HttpPost("users/{id}/enable")]
    [SecureClientPortal.Backend.Api.Security.RequirePermission("users.activate")]
    public async Task<IActionResult> EnableUser(string id, CancellationToken ct)
    {
        return await ExecuteAsync(async () => FromResult(await _service.UpdateUserStatusAsync(id, new AdminUpdateStatusRequest("active"), User, ct)));
    }

    [HttpPost("users/{id}/reset-access")]
    [SecureClientPortal.Backend.Api.Security.RequirePermission("users.update")]
    public async Task<IActionResult> ResetUserAccess(string id, [FromBody] AdminResetAccessRequest request, CancellationToken ct)
    {
        return await ExecuteAsync(async () => FromResult(await _service.ResetUserAccessAsync(id, request, User, ct)));
    }

    [HttpPost("users/{id}/reset-password")]
    [SecureClientPortal.Backend.Api.Security.RequirePermission("users.update")]
    public async Task<IActionResult> ResetPassword(string id, [FromBody] AdminResetPasswordRequest request, CancellationToken ct)
    {
        return await ExecuteAsync(async () => FromResult(await _service.ResetPasswordAsync(id, request, User, ct)));
    }

    [HttpGet("settings/{key}")]
    [SecureClientPortal.Backend.Api.Security.RequirePermission("settings.read")]
    public async Task<IActionResult> GetSetting(string key, CancellationToken ct)
    {
        return Ok(await _service.GetSettingAsync(key, ct));
    }

    [HttpPut("settings/{key}")]
    [SecureClientPortal.Backend.Api.Security.RequirePermission("settings.update")]
    public async Task<IActionResult> PutSetting(string key, [FromBody] AdminSettingRequest request, CancellationToken ct)
    {
        return await ExecuteAsync(async () => Ok(await _service.PutSettingAsync(key, request, User, ct)));
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
