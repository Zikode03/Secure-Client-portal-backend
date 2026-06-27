using Microsoft.AspNetCore.Http;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Models;

namespace SecureClientPortal.Backend.Application.Documents;

public interface IDocumentQueryService
{
    Task<IReadOnlyList<Document>> GetAllAsync(System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default);
    Task<ServiceResult<IReadOnlyList<Document>>> GetFilingRegisterAsync(System.Security.Claims.ClaimsPrincipal user, string? clientId = null, CancellationToken ct = default);
    Task<IReadOnlyList<FilingRule>> GetFilingRulesAsync(CancellationToken ct = default);
    Task<ServiceResult<object>> GetByIdAsync(string id, System.Security.Claims.ClaimsPrincipal user, HttpContext httpContext, CancellationToken ct = default);
    Task<ServiceResult<IReadOnlyList<object>>> GetVersionsAsync(string id, System.Security.Claims.ClaimsPrincipal user, HttpContext httpContext, CancellationToken ct = default);
    Task<ServiceResult<(StoredFileContent Content, string FileName)>> DownloadAsync(string id, System.Security.Claims.ClaimsPrincipal user, HttpContext httpContext, CancellationToken ct = default);
    Task<ServiceResult<IReadOnlyList<DocumentComment>>> GetCommentsAsync(string id, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default);
}
