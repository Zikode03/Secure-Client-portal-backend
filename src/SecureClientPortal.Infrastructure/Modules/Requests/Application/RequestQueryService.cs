using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts.Modules.Requests;
using SecureClientPortal.Backend.Application.Modules.Requests;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Domain.Modules.Requests;
using SecureClientPortal.Backend.Domain.Shared.Modules.Requests;

namespace SecureClientPortal.Backend.Infrastructure.Modules.Requests.Application;

public sealed class RequestQueryService : IRequestQueryService
{
    private readonly IRequestModuleDbContext _requests;
    private readonly PortalDbContext _db;

    public RequestQueryService(IRequestModuleDbContext requests, PortalDbContext db)
    {
        _requests = requests;
        _db = db;
    }

    public async Task<(bool forbidden, IReadOnlyList<RequestItem> results)> GetAllAsync(System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default)
    {
        await RefreshOverdueRequestsAsync(ct);
        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        var items = await _requests.Requests
            .Where(x => allowedClientIds.Contains(x.ClientId))
            .OrderByDescending(x => x.RequestedAtUtc)
            .ToListAsync(ct);
        return (false, items);
    }

    public async Task<(bool forbidden, RequestItem? item)> GetByIdAsync(string id, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default)
    {
        await RefreshOverdueRequestsAsync(ct);
        if (!Guid.TryParse(id, out var requestId))
        {
            return (false, null);
        }

        var item = await _requests.Requests.FindAsync([requestId], ct);
        if (item is null)
        {
            return (false, null);
        }

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        return allowedClientIds.Contains(item.ClientId) ? (false, item) : (true, null);
    }

    public async Task<(bool forbidden, IReadOnlyList<RequestComment>? comments)> GetCommentsAsync(string id, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default)
    {
        if (!Guid.TryParse(id, out var requestId))
        {
            return (false, null);
        }

        var item = await _requests.Requests.FindAsync([requestId], ct);
        if (item is null)
        {
            return (false, null);
        }

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(item.ClientId))
        {
            return (true, null);
        }

        var comments = await _requests.RequestComments
            .Where(x => x.RequestId == item.Id)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(ct);

        return (false, comments);
    }

    public async Task<ServiceResult<RequestWorkspaceResponse>> GetWorkspaceAsync(string id, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default)
    {
        await RefreshOverdueRequestsAsync(ct);

        if (!Guid.TryParse(id, out var requestId))
        {
            return ServiceResult<RequestWorkspaceResponse>.NotFoundResult();
        }

        var item = await _requests.Requests.FirstOrDefaultAsync(x => x.Id == requestId, ct);
        if (item is null)
        {
            return ServiceResult<RequestWorkspaceResponse>.NotFoundResult();
        }

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(item.ClientId))
        {
            return ServiceResult<RequestWorkspaceResponse>.ForbiddenResult();
        }

        var comments = await _requests.RequestComments
            .Where(x => x.RequestId == item.Id)
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => new RequestWorkspaceCommentResponse(
                x.Id,
                x.RequestId,
                x.ClientId,
                x.AuthorUserId,
                x.AuthorRole,
                x.Message,
                x.CreatedAtUtc))
            .ToListAsync(ct);

        RequestWorkspaceRelatedDocumentResponse? relatedDocument = null;
        if (item.RelatedDocumentId.HasValue)
        {
            var document = await _requests.Documents
                .Where(x => x.Id == item.RelatedDocumentId.Value && x.ClientId == item.ClientId)
                .Select(x => new
                {
                    x.Id,
                    x.ClientId,
                    x.MonthlyPackId,
                    x.DocumentSlotId,
                    x.Name,
                    x.Category,
                    x.Status,
                    x.FileType,
                    x.SizeBytes,
                    x.CurrentVersionNumber,
                    x.UploadedAtUtc,
                    x.UpdatedAtUtc
                })
                .FirstOrDefaultAsync(ct);

            if (document is not null)
            {
                var versions = await _db.DocumentVersions
                    .Where(x => x.DocumentId == document.Id)
                    .OrderByDescending(x => x.VersionNumber)
                    .ThenByDescending(x => x.CreatedAtUtc)
                    .Select(x => new RequestWorkspaceDocumentVersionResponse(
                        x.Id,
                        x.DocumentId,
                        x.VersionNumber,
                        x.Name,
                        x.OriginalFileName,
                        x.StoredFileName,
                        x.FileType,
                        x.SizeBytes,
                        x.IsCurrentVersion,
                        x.CreatedAtUtc))
                    .ToListAsync(ct);

                relatedDocument = new RequestWorkspaceRelatedDocumentResponse(
                    document.Id,
                    document.ClientId,
                    document.MonthlyPackId,
                    document.DocumentSlotId,
                    document.Name,
                    document.Category,
                    document.Status,
                    document.FileType,
                    document.SizeBytes,
                    document.CurrentVersionNumber,
                    document.UploadedAtUtc,
                    document.UpdatedAtUtc,
                    $"/api/documents/{document.Id}/download",
                    versions);
            }
        }

        await _db.WriteAuditLogAsync(user, "request.opened", "request", item.Id, item.ClientId, null, ct);

        return ServiceResult<RequestWorkspaceResponse>.Success(new RequestWorkspaceResponse(
            new RequestWorkspaceItemResponse(
                item.Id,
                item.ClientId,
                item.RequestType,
                item.RelatedDocumentId,
                item.Title,
                item.Description,
                item.Priority,
                item.Status,
                item.DueDateUtc,
                item.RequestedByUserId,
                item.ResolvedByUserId,
                item.RequestedAtUtc,
                item.ResolvedAtUtc,
                item.UpdatedAtUtc),
            comments,
            relatedDocument,
            item.Status != RequestStatus.Resolved.ToStorageValue() &&
            item.RequestType == "reupload_required" &&
            relatedDocument is not null));
    }

    private async Task RefreshOverdueRequestsAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var overdueRequests = await _requests.Requests
            .Where(x =>
                x.Status != RequestStatus.Resolved.ToStorageValue() &&
                x.DueDateUtc != null &&
                x.DueDateUtc < now &&
                x.Status != RequestStatus.Overdue.ToStorageValue())
            .ToListAsync(ct);

        if (overdueRequests.Count == 0)
        {
            return;
        }

        RequestWorkflowPolicy.RefreshOverdue(overdueRequests, now);
        await _requests.SaveChangesAsync(ct);
    }
}
