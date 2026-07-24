using Microsoft.AspNetCore.Http;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Domain.Modules.MonthlyPacks;
using System.Security.Claims;

namespace SecureClientPortal.Backend.Application.Modules.MonthlyPacks;

public interface IDocumentSlotService
{
    Task<(bool forbidden, IReadOnlyList<DocumentSlot>? items)> GetByMonthlyPackIdAsync(string monthlyPackId, ClaimsPrincipal user, CancellationToken ct = default);
    Task<(bool forbidden, DocumentSlot created)> CreateAsync(CreateDocumentSlotRequest request, ClaimsPrincipal user, CancellationToken ct = default);
    Task<(bool forbidden, bool invalid, string? error, DocumentSlot? slot)> SubmitAsync(string slotId, ClaimsPrincipal user, CancellationToken ct = default);
    Task<(bool forbidden, bool invalid, string? error, DocumentSlot? slot)> MarkNotApplicableAsync(string slotId, ClaimsPrincipal user, CancellationToken ct = default);
    Task<ServiceResult<DocumentSlot>> UploadAsync(string slotId, UploadDocumentSlotRequest request, ClaimsPrincipal user, CancellationToken ct = default);
    Task<ServiceResult<IReadOnlyList<object>>> GetVersionsAsync(string slotId, ClaimsPrincipal user, HttpContext httpContext, CancellationToken ct = default);
    Task<ServiceResult<DocumentSlotWorkspaceResponse>> GetWorkspaceAsync(string slotId, ClaimsPrincipal user, HttpContext httpContext, CancellationToken ct = default);
    Task<ServiceResult<object>> StartReviewAsync(string slotId, StartDocumentSlotReviewRequest request, ClaimsPrincipal user, CancellationToken ct = default);
    Task<ServiceResult<object>> ApproveAsync(string slotId, ApproveDocumentSlotRequest request, ClaimsPrincipal user, CancellationToken ct = default);
    Task<ServiceResult<object>> RejectAsync(string slotId, RejectDocumentSlotRequest request, ClaimsPrincipal user, CancellationToken ct = default);
    Task<ServiceResult<object>> RequestReuploadAsync(string slotId, RequestDocumentSlotReuploadRequest request, ClaimsPrincipal user, CancellationToken ct = default);
}
