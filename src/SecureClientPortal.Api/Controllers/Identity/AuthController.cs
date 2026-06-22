using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts;
using SecureClientPortal.Backend.Application.Identity;

namespace SecureClientPortal.Backend.Controllers;

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
