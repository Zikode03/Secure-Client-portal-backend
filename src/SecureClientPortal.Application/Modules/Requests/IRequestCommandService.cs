using SecureClientPortal.Backend.Application.Contracts.Modules.Requests;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Models;

namespace SecureClientPortal.Backend.Application.Modules.Requests;

public interface IRequestCommandService
{
    Task<(bool forbidden, RequestItem created)> CreateAsync(CreateRequestRequest request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default);
    Task<(bool forbidden, RequestItem? updated)> UpdateAsync(string id, UpdateRequestRequest request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default);
    Task<(bool forbidden, RequestItem? updated)> UpdateStatusAsync(string id, UpdateRequestStatusRequest request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default);
    Task<(bool forbidden, RequestComment? comment)> AddCommentAsync(string id, AddRequestCommentRequest request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default);
    Task<(bool forbidden, RequestItem? resolved)> ResolveAsync(string id, ResolveRequestRequest request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default);
    Task<ServiceResult<RequestItem>> EscalateAsync(string id, EscalateRequestRequest request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default);
    Task<(bool forbidden, bool deleted)> DeleteAsync(string id, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default);
    Task<ServiceResult<RequestItem>> MarkDocumentUploadedAsync(string id, string? message, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default);
}
