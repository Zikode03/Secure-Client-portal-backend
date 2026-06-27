using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts;
using SecureClientPortal.Backend.Models;

namespace SecureClientPortal.Backend.Application.Documents;

public interface IDocumentCommandService
{
    Task<ServiceResult<FilingRule>> UpdateFilingRuleAsync(string category, FilingRuleUpdateRequest request, CancellationToken ct = default);
    Task<ServiceResult<object>> UploadAsync(UploadDocumentRequest request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default);
    Task<ServiceResult<Document>> CreateAsync(CreateDocumentRequest request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default);
    Task<ServiceResult<Document>> UpdateAsync(string id, UpdateDocumentRequest request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default);
    Task<ServiceResult<DocumentComment>> AddCommentAsync(string id, AddDocumentCommentRequest request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default);
    Task<ServiceResult<bool>> DeleteAsync(string id, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default);
}
