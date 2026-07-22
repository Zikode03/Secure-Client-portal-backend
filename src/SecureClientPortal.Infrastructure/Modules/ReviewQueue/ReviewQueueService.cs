using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts.Modules.ReviewQueue;
using SecureClientPortal.Backend.Application.Modules.ReviewQueue;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Domain.Modules.Documents;
using System.Security.Claims;
using System.Text.Json;

namespace SecureClientPortal.Backend.Infrastructure.Modules.ReviewQueue;

public sealed class ReviewQueueService : IReviewQueueService
{
    private sealed record PendingReviewProjection(
        Guid DocumentId,
        Guid ClientId,
        string ClientName,
        Guid MonthlyPackId,
        int Year,
        int Month,
        Guid? DocumentSlotId,
        string? SlotLabel,
        string DocumentName,
        string DocumentCategory,
        string DocumentStatus,
        string? SlotStatus,
        int CurrentVersionNumber,
        DateTime UploadedAtUtc,
        DateTime? SubmittedAtUtc,
        string? RejectionReason);

    private readonly PortalDbContext _db;

    public ReviewQueueService(PortalDbContext db)
    {
        _db = db;
    }

    public async Task<(bool forbidden, IReadOnlyList<ReviewQueueItemResponse> items)> GetPendingAsync(
        ClaimsPrincipal user,
        ReviewQueueFilterRequest? filter = null,
        CancellationToken ct = default)
    {
        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        var normalizedCategory = filter?.DocumentCategory?.Trim().ToLowerInvariant();
        var normalizedStatus = filter?.SlotStatus?.Trim().ToLowerInvariant();
        var normalizedPriority = filter?.Priority?.Trim().ToLowerInvariant();
        var normalizedSort = filter?.Sort?.Trim().ToLowerInvariant() == "oldest" ? "oldest" : "newest";
        var requestedClientId = filter?.ClientId;

        if (requestedClientId.HasValue && requestedClientId.Value != Guid.Empty && !allowedClientIds.Contains(requestedClientId.Value))
        {
            return (true, []);
        }

        var items = await
            (from document in _db.Documents
             join client in _db.Clients on document.ClientId equals client.Id
             join pack in _db.MonthlyPacks on document.MonthlyPackId equals pack.Id
             join slotLeft in _db.DocumentSlots on document.DocumentSlotId equals slotLeft.Id into slotGroup
             from slot in slotGroup.DefaultIfEmpty()
             where allowedClientIds.Contains(document.ClientId)
                 && (!requestedClientId.HasValue || document.ClientId == requestedClientId.Value)
                 && (normalizedCategory == null || document.Category == normalizedCategory)
                 && (normalizedStatus == null || slot!.Status == normalizedStatus)
                 && (slot != null && (slot.Status == "submitted" || slot.Status == "under_review"))
             select new PendingReviewProjection(
                 document.Id,
                 client.Id,
                 client.Name,
                 pack.Id,
                 pack.Year,
                 pack.Month,
                 slot!.Id,
                 slot.Label,
                 document.Name,
                 document.Category,
                 document.Status,
                 slot.Status,
                 document.CurrentVersionNumber,
                 document.UploadedAtUtc,
                 slot.SubmittedAtUtc,
                 slot.RejectionReason))
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        var filtered = items
            .Select(item =>
            {
                var reviewAgeDays = Math.Max(0, (int)Math.Floor((now - (item.SubmittedAtUtc ?? item.UploadedAtUtc)).TotalDays));
                var reviewPriority = GetPriority(reviewAgeDays);
                return new ReviewQueueItemResponse(
                    item.DocumentId,
                    item.ClientId,
                    item.ClientName,
                    item.MonthlyPackId,
                    item.Year,
                    item.Month,
                    item.DocumentSlotId,
                    item.SlotLabel,
                    item.DocumentName,
                    item.DocumentCategory,
                    item.DocumentStatus,
                    item.SlotStatus,
                    reviewPriority,
                    reviewAgeDays,
                    item.CurrentVersionNumber,
                    item.UploadedAtUtc,
                    item.SubmittedAtUtc,
                    item.RejectionReason);
            });

        if (filter?.MinAgeDays is int minAgeDays)
        {
            filtered = filtered.Where(x => x.ReviewAgeDays >= Math.Max(0, minAgeDays));
        }

        if (!string.IsNullOrWhiteSpace(normalizedPriority))
        {
            filtered = filtered.Where(x => x.ReviewPriority == normalizedPriority);
        }

        filtered = normalizedSort == "oldest"
            ? filtered.OrderByDescending(x => x.ReviewAgeDays).ThenBy(x => x.SubmittedAtUtc ?? x.UploadedAtUtc)
            : filtered.OrderByDescending(x => x.SubmittedAtUtc ?? x.UploadedAtUtc);

        return (false, filtered.ToList());
    }

    public async Task<ServiceResult<ReviewQueueWorkspaceResponse>> GetWorkspaceAsync(
        string documentId,
        ClaimsPrincipal user,
        HttpContext httpContext,
        CancellationToken ct = default)
    {
        if (!Guid.TryParse(documentId, out var parsedDocumentId))
        {
            return ServiceResult<ReviewQueueWorkspaceResponse>.NotFoundResult();
        }

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        var item = await
            (from document in _db.Documents
             join client in _db.Clients on document.ClientId equals client.Id
             join pack in _db.MonthlyPacks on document.MonthlyPackId equals pack.Id
             join slotLeft in _db.DocumentSlots on document.DocumentSlotId equals slotLeft.Id into slotGroup
             from slot in slotGroup.DefaultIfEmpty()
             where document.Id == parsedDocumentId
             select new PendingReviewProjection(
                 document.Id,
                 client.Id,
                 client.Name,
                 pack.Id,
                 pack.Year,
                 pack.Month,
                 slot != null ? slot.Id : null,
                 slot != null ? slot.Label : null,
                 document.Name,
                 document.Category,
                 document.Status,
                 slot != null ? slot.Status : null,
                 document.CurrentVersionNumber,
                 document.UploadedAtUtc,
                 slot != null ? slot.SubmittedAtUtc : null,
                 slot != null ? slot.RejectionReason : null))
            .FirstOrDefaultAsync(ct);

        if (item is null)
        {
            return ServiceResult<ReviewQueueWorkspaceResponse>.NotFoundResult();
        }

        if (!allowedClientIds.Contains(item.ClientId))
        {
            return ServiceResult<ReviewQueueWorkspaceResponse>.ForbiddenResult();
        }

        var now = DateTime.UtcNow;
        var reviewAgeDays = Math.Max(0, (int)Math.Floor((now - (item.SubmittedAtUtc ?? item.UploadedAtUtc)).TotalDays));
        var queueItem = new ReviewQueueItemResponse(
            item.DocumentId,
            item.ClientId,
            item.ClientName,
            item.MonthlyPackId,
            item.Year,
            item.Month,
            item.DocumentSlotId,
            item.SlotLabel,
            item.DocumentName,
            item.DocumentCategory,
            item.DocumentStatus,
            item.SlotStatus,
            GetPriority(reviewAgeDays),
            reviewAgeDays,
            item.CurrentVersionNumber,
            item.UploadedAtUtc,
            item.SubmittedAtUtc,
            item.RejectionReason);

        var versions = await _db.DocumentVersions
            .Where(x => x.DocumentId == parsedDocumentId)
            .OrderByDescending(x => x.VersionNumber)
            .ThenByDescending(x => x.CreatedAtUtc)
            .Select(x => new ReviewQueueVersionResponse(
                x.Id,
                x.DocumentId,
                x.VersionNumber,
                x.Name,
                x.OriginalFileName,
                x.StoredFileName,
                x.FileType,
                x.SizeBytes,
                x.IsCurrentVersion,
                x.UploadedByUserId,
                x.CreatedAtUtc))
            .ToListAsync(ct);

        var comments = await _db.DocumentComments
            .Where(x => x.DocumentId == parsedDocumentId)
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => new ReviewQueueCommentResponse(
                x.Id,
                x.DocumentId,
                x.AuthorUserId,
                x.AuthorRole,
                x.Message,
                x.CreatedAtUtc))
            .ToListAsync(ct);

        var reviewHistory = await _db.ReviewDecisions
            .Where(x => x.DocumentId == parsedDocumentId)
            .OrderByDescending(x => x.DecidedAtUtc)
            .Select(x => new ReviewQueueDecisionResponse(
                x.Id,
                x.DocumentId,
                x.Decision,
                x.ReviewerUserId,
                x.ReviewerRole,
                x.Reason,
                x.InternalNote,
                x.DecidedAtUtc))
            .ToListAsync(ct);

        var documentEntity = await _db.Documents.FirstAsync(x => x.Id == parsedDocumentId, ct);
        await _db.WriteAuditLogAsync(
            user,
            "review_queue.opened",
            "document",
            documentEntity.Id,
            documentEntity.ClientId,
            JsonSerializer.Serialize(new { documentEntity.Id, queueItem.SlotStatus, queueItem.ReviewPriority }),
            ct);
        await _db.WriteDocumentAccessLogAsync(
            user,
            httpContext,
            documentEntity,
            "review_queue_open",
            JsonSerializer.Serialize(new { documentEntity.Id, queueItem.SlotStatus, queueItem.ReviewPriority }),
            ct);

        return ServiceResult<ReviewQueueWorkspaceResponse>.Success(new ReviewQueueWorkspaceResponse(
            queueItem,
            $"/api/documents/{parsedDocumentId}/download",
            versions,
            comments,
            reviewHistory));
    }

    private static string GetPriority(int reviewAgeDays)
    {
        if (reviewAgeDays >= 7)
        {
            return "urgent";
        }

        if (reviewAgeDays >= 3)
        {
            return "high";
        }

        return "normal";
    }
}
