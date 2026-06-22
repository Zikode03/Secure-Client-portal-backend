using SecureClientPortal.Backend.Application.Contracts;

namespace SecureClientPortal.Backend.Application.Assignments;

public interface IAssignmentService
{
    Task<(bool forbidden, IReadOnlyList<object> results)> GetAllAsync(System.Security.Claims.ClaimsPrincipal user, string? clientId, CancellationToken ct = default);
    Task<object> CreateAsync(CreateAssignmentRequest request, CurrentUserContext actor, CancellationToken ct = default);
    Task<bool> DeleteAsync(string id, CurrentUserContext actor, CancellationToken ct = default);
    Task<object> ReassignAsync(ReassignAccountantRequest request, CurrentUserContext actor, CancellationToken ct = default);
    Task<object?> MakePrimaryAsync(string id, CurrentUserContext actor, CancellationToken ct = default);
}
