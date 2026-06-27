using Microsoft.AspNetCore.Http;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts;
using SecureClientPortal.Backend.Models;

namespace SecureClientPortal.Backend.Application.Documents;

public interface IDocumentWorkflowService
{
    Task<IReadOnlyList<Document>> GetAllAsync(System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default);
    Task<ServiceResult<IReadOnlyList<Document>>> GetFilingRegisterAsync(System.Security.Claims.ClaimsPrincipal user, string? clientId = null, CancellationToken ct = default);
    Task<IReadOnlyList<FilingRule>> GetFilingRulesAsync(CancellationToken ct = default);
    Task<ServiceResult<FilingRule>> UpdateFilingRuleAsync(string category, FilingRuleUpdateRequest request, CancellationToken ct = default);
    Task<ServiceResult<object>> UploadAsync(UploadDocumentRequest request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default);
    Task<ServiceResult<Document>> CreateAsync(CreateDocumentRequest request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default);
    Task<ServiceResult<object>> GetByIdAsync(string id, System.Security.Claims.ClaimsPrincipal user, HttpContext httpContext, CancellationToken ct = default);
    Task<ServiceResult<IReadOnlyList<object>>> GetVersionsAsync(string id, System.Security.Claims.ClaimsPrincipal user, HttpContext httpContext, CancellationToken ct = default);
    Task<ServiceResult<(StoredFileContent Content, string FileName)>> DownloadAsync(string id, System.Security.Claims.ClaimsPrincipal user, HttpContext httpContext, CancellationToken ct = default);
    Task<ServiceResult<Document>> UpdateAsync(string id, UpdateDocumentRequest request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default);
    Task<ServiceResult<Document>> UpdateStatusAsync(string id, UpdateDocumentStatusRequest request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default);
    Task<ServiceResult<object>> ReviewAsync(string id, AddReviewDecisionRequest request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default);
    Task<ServiceResult<object>> RequestReuploadAsync(string id, RequestReuploadRequest request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default);
    Task<ServiceResult<DocumentComment>> AddCommentAsync(string id, AddDocumentCommentRequest request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default);
    Task<ServiceResult<IReadOnlyList<DocumentComment>>> GetCommentsAsync(string id, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default);
    Task<ServiceResult<bool>> DeleteAsync(string id, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default);
}
