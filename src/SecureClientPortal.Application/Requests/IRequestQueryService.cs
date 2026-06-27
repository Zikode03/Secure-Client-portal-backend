using SecureClientPortal.Backend.Models;

namespace SecureClientPortal.Backend.Application.Requests;

public interface IRequestQueryService
{
    Task<(bool forbidden, IReadOnlyList<RequestItem> results)> GetAllAsync(System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default);
    Task<(bool forbidden, RequestItem? item)> GetByIdAsync(string id, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default);
    Task<(bool forbidden, IReadOnlyList<RequestComment>? comments)> GetCommentsAsync(string id, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default);
}
