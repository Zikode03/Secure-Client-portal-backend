using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts.Modules.Auth;
using SecureClientPortal.Backend.Application.Modules.Auth;

namespace SecureClientPortal.Backend.Api.Modules.Auth;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _service;

    public AuthController(IAuthService service)
    {
        _service = service;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        return await ExecuteAsync(async () => FromResult(await _service.LoginAsync(request, HttpContext, ct)));
    }

    [HttpPost("complete-invite")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-recovery")]
    public async Task<IActionResult> CompleteInvite([FromBody] CompleteInviteRequest request, CancellationToken ct)
    {
        return await ExecuteAsync(async () => FromResult(await _service.CompleteInviteAsync(request, HttpContext, ct)));
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-recovery")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken ct)
    {
        return await ExecuteAsync(async () => FromResult(await _service.ForgotPasswordAsync(request, HttpContext, ct)));
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest request, CancellationToken ct)
    {
        return await ExecuteAsync(async () => FromResult(await _service.RefreshAsync(request, HttpContext, ct)));
    }

    [HttpPost("change-password")]
    [Authorize]
    [EnableRateLimiting("auth-account")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken ct)
    {
        return await ExecuteAsync(async () => FromResult(await _service.ChangePasswordAsync(request, User, HttpContext, ct)));
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await _service.LogoutAsync(User, ct);
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        return await ExecuteAsync(async () => FromResult(await _service.MeAsync(User, ct)));
    }

    [HttpGet("security")]
    [Authorize]
    public async Task<IActionResult> Security(CancellationToken ct)
    {
        return await ExecuteAsync(async () => FromResult(await _service.GetSecuritySettingsAsync(User, ct)));
    }

    [HttpPut("security")]
    [Authorize]
    [EnableRateLimiting("auth-account")]
    public async Task<IActionResult> UpdateSecurity([FromBody] UpdateSecuritySettingsRequest request, CancellationToken ct)
    {
        return await ExecuteAsync(async () => FromResult(await _service.UpdateSecuritySettingsAsync(request, User, ct)));
    }

    [HttpGet("sessions")]
    [Authorize]
    public async Task<IActionResult> Sessions(CancellationToken ct)
    {
        return await ExecuteAsync(async () => FromResult(await _service.GetSessionsAsync(User, ct)));
    }

    [HttpDelete("sessions/{sessionId:guid}")]
    [Authorize]
    [EnableRateLimiting("auth-account")]
    public async Task<IActionResult> RevokeSession(Guid sessionId, CancellationToken ct)
    {
        return await ExecuteAsync(async () => FromResult(await _service.RevokeSessionAsync(sessionId, User, ct)));
    }

    [HttpPost("sessions/revoke-others")]
    [Authorize]
    [EnableRateLimiting("auth-account")]
    public async Task<IActionResult> RevokeOtherSessions(CancellationToken ct)
    {
        return await ExecuteAsync(async () => FromResult(await _service.RevokeOtherSessionsAsync(User, ct)));
    }

    private async Task<IActionResult> ExecuteAsync(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (AppValidationException ex)
        {
            return BadRequest(new { code = "VALIDATION_ERROR", message = ex.Message, errors = ex.Errors });
        }
    }

    private ActionResult FromResult(ServiceResult<object> result)
    {
        if (result.Forbidden)
        {
            return StatusCode(result.StatusCode ?? StatusCodes.Status403Forbidden, new { code = result.ErrorCode, message = result.Error });
        }

        if (result.NotFound)
        {
            return StatusCode(result.StatusCode ?? StatusCodes.Status404NotFound, new { code = result.ErrorCode, message = result.Error ?? "Resource was not found." });
        }

        if (result.Unauthorized)
        {
            return StatusCode(result.StatusCode ?? StatusCodes.Status401Unauthorized, new { code = result.ErrorCode, message = result.Error });
        }

        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            return StatusCode(result.StatusCode ?? StatusCodes.Status400BadRequest, new { code = result.ErrorCode, message = result.Error });
        }

        return Ok(result.Value);
    }
}
