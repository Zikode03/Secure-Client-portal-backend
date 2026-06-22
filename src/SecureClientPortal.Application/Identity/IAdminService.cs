using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts;

namespace SecureClientPortal.Backend.Application.Identity;

public interface IAdminService
{
    Task<IReadOnlyList<object>> GetUsersAsync(CancellationToken ct = default);
    Task<ServiceResult<object>> CreateUserAsync(AdminCreateUserRequest request, System.Security.Claims.ClaimsPrincipal actor, CancellationToken ct = default);
    Task<ServiceResult<object>> UpdateUserRoleAsync(string id, AdminUpdateRoleRequest request, System.Security.Claims.ClaimsPrincipal actor, CancellationToken ct = default);
    Task<ServiceResult<object>> UpdateUserStatusAsync(string id, AdminUpdateStatusRequest request, System.Security.Claims.ClaimsPrincipal actor, CancellationToken ct = default);
    Task<ServiceResult<object>> ResetUserAccessAsync(string id, AdminResetAccessRequest request, System.Security.Claims.ClaimsPrincipal actor, CancellationToken ct = default);
    Task<ServiceResult<object>> ResetPasswordAsync(string id, AdminResetPasswordRequest request, System.Security.Claims.ClaimsPrincipal actor, CancellationToken ct = default);
    Task<object> GetSettingAsync(string key, CancellationToken ct = default);
    Task<object> PutSettingAsync(string key, AdminSettingRequest request, CancellationToken ct = default);
}
