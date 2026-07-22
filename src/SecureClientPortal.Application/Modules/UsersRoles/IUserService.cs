using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts.Modules.UsersRoles;

namespace SecureClientPortal.Backend.Application.Modules.UsersRoles;

public interface IUserService
{
    Task<IReadOnlyList<object>> GetAllAsync(CancellationToken ct = default);
    Task<ServiceResult<object>> CreateAsync(CreateUserRequest request, System.Security.Claims.ClaimsPrincipal actor, CancellationToken ct = default);
    Task<ServiceResult<object>> UpdateActivationAsync(string id, UpdateUserActivationRequest request, System.Security.Claims.ClaimsPrincipal actor, CancellationToken ct = default);
}
