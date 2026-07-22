using Microsoft.AspNetCore.Http;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts.Modules.ReviewQueue;
using System.Security.Claims;

namespace SecureClientPortal.Backend.Application.Modules.ReviewQueue;

public interface IReviewQueueService
{
    Task<(bool forbidden, IReadOnlyList<ReviewQueueItemResponse> items)> GetPendingAsync(
        ClaimsPrincipal user,
        ReviewQueueFilterRequest? filter = null,
        CancellationToken ct = default);

    Task<ServiceResult<ReviewQueueWorkspaceResponse>> GetWorkspaceAsync(
        string documentId,
        ClaimsPrincipal user,
        HttpContext httpContext,
        CancellationToken ct = default);
}
