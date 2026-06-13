using SecureClientPortal.Backend.Application.Contracts;

namespace SecureClientPortal.Backend.Application.Roles;

public interface IRoleService
{
    Task<IReadOnlyList<object>> GetAllAsync(CancellationToken ct = default);
    Task<object> CreateAsync(CreateRoleRequest request, CurrentUserContext actor, CancellationToken ct = default);
    Task<object?> UpdateAsync(string name, UpdateRoleRequest request, CurrentUserContext actor, CancellationToken ct = default);
    Task<object?> UpdateActivationAsync(string name, bool isActive, CurrentUserContext actor, CancellationToken ct = default);
}
