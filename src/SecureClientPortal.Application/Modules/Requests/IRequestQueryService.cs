using SecureClientPortal.Backend.Models;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts.Modules.Requests;

namespace SecureClientPortal.Backend.Application.Modules.Requests;

public interface IRequestQueryService
{
    Task<(bool forbidden, IReadOnlyList<RequestItem> results)> GetAllAsync(System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default);
    Task<(bool forbidden, RequestItem? item)> GetByIdAsync(string id, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default);
    Task<(bool forbidden, IReadOnlyList<RequestComment>? comments)> GetCommentsAsync(string id, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default);
    Task<ServiceResult<RequestWorkspaceResponse>> GetWorkspaceAsync(string id, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default);
}
