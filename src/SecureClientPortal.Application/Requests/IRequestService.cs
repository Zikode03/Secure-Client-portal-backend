using SecureClientPortal.Backend.Application.Contracts;
using SecureClientPortal.Backend.Models;
using System.Security.Claims;

namespace SecureClientPortal.Backend.Application.Requests;

public interface IRequestService
{
    Task<(bool forbidden, IReadOnlyList<RequestItem> results)> GetAllAsync(ClaimsPrincipal user, CancellationToken ct = default);
    Task<(bool forbidden, RequestItem? item)> GetByIdAsync(string id, ClaimsPrincipal user, CancellationToken ct = default);
    Task<(bool forbidden, RequestItem created)> CreateAsync(CreateRequestRequest request, ClaimsPrincipal user, CancellationToken ct = default);
    Task<(bool forbidden, RequestItem? updated)> UpdateAsync(string id, UpdateRequestRequest request, ClaimsPrincipal user, CancellationToken ct = default);
    Task<(bool forbidden, RequestItem? updated)> UpdateStatusAsync(string id, UpdateRequestStatusRequest request, ClaimsPrincipal user, CancellationToken ct = default);
    Task<(bool forbidden, IReadOnlyList<RequestComment>? comments)> GetCommentsAsync(string id, ClaimsPrincipal user, CancellationToken ct = default);
    Task<(bool forbidden, RequestComment? comment)> AddCommentAsync(string id, AddRequestCommentRequest request, ClaimsPrincipal user, CancellationToken ct = default);
    Task<(bool forbidden, RequestItem? resolved)> ResolveAsync(string id, ResolveRequestRequest request, ClaimsPrincipal user, CancellationToken ct = default);
    Task<(bool forbidden, bool deleted)> DeleteAsync(string id, ClaimsPrincipal user, CancellationToken ct = default);
}
