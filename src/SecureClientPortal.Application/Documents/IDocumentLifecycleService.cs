using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts;
using SecureClientPortal.Backend.Models;

namespace SecureClientPortal.Backend.Application.Documents;

public interface IDocumentLifecycleService
{
    Task<ServiceResult<Document>> UpdateStatusAsync(string id, UpdateDocumentStatusRequest request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default);
    Task<ServiceResult<object>> ReviewAsync(string id, AddReviewDecisionRequest request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default);
    Task<ServiceResult<object>> RequestReuploadAsync(string id, RequestReuploadRequest request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default);
}
