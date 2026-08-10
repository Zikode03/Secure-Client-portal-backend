using Microsoft.AspNetCore.Http;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts.Modules.Auth;

namespace SecureClientPortal.Backend.Application.Modules.Auth;

public interface IAuthService
{
    Task<ServiceResult<object>> LoginAsync(LoginRequest request, HttpContext httpContext, CancellationToken ct = default);
    Task<ServiceResult<object>> CompleteInviteAsync(CompleteInviteRequest request, HttpContext httpContext, CancellationToken ct = default);
    Task<ServiceResult<object>> ForgotPasswordAsync(ForgotPasswordRequest request, HttpContext httpContext, CancellationToken ct = default);
    Task<ServiceResult<object>> RefreshAsync(RefreshTokenRequest request, HttpContext httpContext, CancellationToken ct = default);
    Task<ServiceResult<object>> ChangePasswordAsync(ChangePasswordRequest request, System.Security.Claims.ClaimsPrincipal actor, HttpContext httpContext, CancellationToken ct = default);
    Task LogoutAsync(System.Security.Claims.ClaimsPrincipal actor, CancellationToken ct = default);
    Task<ServiceResult<object>> MeAsync(System.Security.Claims.ClaimsPrincipal actor, CancellationToken ct = default);
    Task<ServiceResult<object>> GetSecuritySettingsAsync(System.Security.Claims.ClaimsPrincipal actor, CancellationToken ct = default);
    Task<ServiceResult<object>> UpdateSecuritySettingsAsync(UpdateSecuritySettingsRequest request, System.Security.Claims.ClaimsPrincipal actor, CancellationToken ct = default);
    Task<ServiceResult<object>> GetSessionsAsync(System.Security.Claims.ClaimsPrincipal actor, CancellationToken ct = default);
    Task<ServiceResult<object>> RevokeSessionAsync(Guid sessionId, System.Security.Claims.ClaimsPrincipal actor, CancellationToken ct = default);
    Task<ServiceResult<object>> RevokeOtherSessionsAsync(System.Security.Claims.ClaimsPrincipal actor, CancellationToken ct = default);
}
