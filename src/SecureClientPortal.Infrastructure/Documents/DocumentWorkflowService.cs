using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts;
using SecureClientPortal.Backend.Application.Documents;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using StorageFileStorage = SecureClientPortal.Backend.Storage.IFileStorage;

namespace SecureClientPortal.Backend.Infrastructure.Documents;

public sealed class DocumentWorkflowService : IDocumentWorkflowService
{
    private readonly PortalDbContext _db;
    private readonly StorageFileStorage _fileStorage;

    public DocumentWorkflowService(PortalDbContext db, StorageFileStorage fileStorage)
    {
        _db = db;
        _fileStorage = fileStorage;
    }

    public async Task<IReadOnlyList<Document>> GetAllAsync(System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default)
    {
        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        return await _db.Documents
            .Where(x => allowedClientIds.Contains(x.ClientId))
            .OrderByDescending(x => x.UploadedAtUtc)
            .ToListAsync(ct);
    }

    public async Task<ServiceResult<IReadOnlyList<Document>>> GetFilingRegisterAsync(System.Security.Claims.ClaimsPrincipal user, string? clientId = null, CancellationToken ct = default)
    {
        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        var query = _db.Documents.Where(x => x.IsFiled && allowedClientIds.Contains(x.ClientId));
        if (!string.IsNullOrWhiteSpace(clientId))
        {
            if (!allowedClientIds.Contains(clientId))
            {
                return ServiceResult<IReadOnlyList<Document>>.ForbiddenResult();
            }

            query = query.Where(x => x.ClientId == clientId);
        }

        return ServiceResult<IReadOnlyList<Document>>.Success(
            await query.OrderByDescending(x => x.FiledAtUtc).ThenByDescending(x => x.UploadedAtUtc).ToListAsync(ct));
    }

    public async Task<IReadOnlyList<FilingRule>> GetFilingRulesAsync(CancellationToken ct = default)
    {
        return await _db.FilingRules.OrderBy(x => x.Category).ToListAsync(ct);
    }

    public async Task<ServiceResult<FilingRule>> UpdateFilingRuleAsync(string category, FilingRuleUpdateRequest request, CancellationToken ct = default)
    {
        var normalizedCategory = NormalizeCategory(category);
        var item = await _db.FilingRules.FirstOrDefaultAsync(x => x.Category == normalizedCategory, ct);
        if (item is null)
        {
            return ServiceResult<FilingRule>.NotFoundResult();
        }

        item.Update(item.Category, item.Description, request.IsEnabled);
        await _db.SaveChangesAsync(ct);
        return ServiceResult<FilingRule>.Success(item);
    }

    public async Task<ServiceResult<object>> UploadAsync(UploadDocumentRequest request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default)
    {
        DocumentValidators.ValidateUpload(request);

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(request.ClientId))
        {
            return ServiceResult<object>.ForbiddenResult();
        }

        var normalizedCategory = NormalizeCategory(request.DocumentType);
        var pack = await ResolveMonthlyPackAsync(request.ClientId, request.MonthlyPackId, request.DocumentSlotId, ct);
        if (pack is null)
        {
            return ServiceResult<object>.ErrorResult("A valid monthly pack is required for document upload.");
        }

        var slot = await ResolveSlotAsync(request.ClientId, pack.Id, request.DocumentSlotId, normalizedCategory, ct);
        if (slot is null)
        {
            return ServiceResult<object>.ErrorResult("A valid document slot is required for document upload.");
        }

        var stored = await _fileStorage.SaveAsync(request.File, request.ClientId, ct);
        var actorUserId = user.GetUserId() ?? "unknown";
        var now = DateTime.UtcNow;

        Document document;
        var isNewDocument = string.IsNullOrWhiteSpace(request.DocumentId);
        if (isNewDocument)
        {
            document = Document.CreateUploaded(
                $"doc_{Guid.NewGuid():N}",
                request.ClientId,
                pack.Id,
                stored.OriginalFileName,
                normalizedCategory,
                slot.Id,
                stored.ContentType,
                stored.SizeBytes,
                stored.StorageKey,
                actorUserId);
            _db.Documents.Add(document);
        }
        else
        {
            var existingDocument = await _db.Documents.FirstOrDefaultAsync(x => x.Id == request.DocumentId, ct);
            if (existingDocument is null)
            {
                return ServiceResult<object>.NotFoundResult("Requested document could not be found.");
            }

            document = existingDocument;
            if (!string.Equals(document.ClientId, request.ClientId, StringComparison.OrdinalIgnoreCase))
            {
                return ServiceResult<object>.ForbiddenResult();
            }

            document.ReplaceUpload(
                pack.Id,
                stored.OriginalFileName,
                normalizedCategory,
                slot.Id,
                stored.ContentType,
                stored.SizeBytes,
                stored.StorageKey,
                actorUserId);

            var previousVersions = await _db.DocumentVersions
                .Where(x => x.DocumentId == document.Id && x.IsCurrentVersion)
                .ToListAsync(ct);
            foreach (var previousVersion in previousVersions)
            {
                previousVersion.MarkNotCurrent();
            }
        }

        _db.DocumentVersions.Add(DocumentVersion.Create(
            $"dv_{Guid.NewGuid():N}",
            document.Id,
            document.CurrentVersionNumber,
            stored.OriginalFileName,
            stored.OriginalFileName,
            stored.StoredFileName,
            stored.ContentType,
            stored.SizeBytes,
            stored.StorageKey,
            true,
            actorUserId,
            now));

        slot.MarkUploaded(document.Id);

        if (isNewDocument)
        {
            pack.MarkInProgress();
        }
        else
        {
            pack.Reopen();
        }

        await _db.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(
            user,
            "documents.uploaded",
            "document",
            document.Id,
            document.ClientId,
            JsonSerializer.Serialize(new
            {
                document.Id,
                document.ClientId,
                monthlyPackId = pack.Id,
                documentSlotId = slot.Id,
                versionNumber = document.CurrentVersionNumber,
                document.StorageKey
            }),
            ct);
        await _db.WriteAuditLogAsync(
            user,
            "documents.version_created",
            "document_version",
            $"{document.Id}:v{document.CurrentVersionNumber}",
            document.ClientId,
            JsonSerializer.Serialize(new
            {
                document.Id,
                document.CurrentVersionNumber,
                stored.OriginalFileName,
                stored.StoredFileName,
                stored.ContentType
            }),
            ct);

        return ServiceResult<object>.Success(await BuildDocumentResponseAsync(document, ct));
    }

    public async Task<ServiceResult<Document>> CreateAsync(Document request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default)
    {
        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(request.ClientId))
        {
            return ServiceResult<Document>.ForbiddenResult();
        }

        var created = Document.CreateUploaded(
            string.IsNullOrWhiteSpace(request.Id) ? $"doc_{Guid.NewGuid():N}" : request.Id,
            request.ClientId,
            request.MonthlyPackId,
            request.Name,
            NormalizeCategory(request.Category),
            request.DocumentSlotId,
            request.FileType,
            request.SizeBytes,
            request.StorageKey,
            request.UploadedByUserId);

        switch (NormalizeDocumentStatus(request.Status))
        {
            case "under_review":
                created.MarkUnderReview();
                break;
            case "accepted":
                created.Accept();
                break;
            case "rejected":
                created.Reject();
                break;
            case "filed":
                created.File(request.UploadedByUserId);
                break;
        }

        _db.Documents.Add(created);
        _db.DocumentVersions.Add(DocumentVersion.Create(
            $"dv_{Guid.NewGuid():N}",
            created.Id,
            1,
            created.Name,
            created.Name,
            Path.GetFileName(created.StorageKey ?? created.Name),
            created.FileType,
            created.SizeBytes,
            created.StorageKey,
            true,
            created.UploadedByUserId,
            created.UploadedAtUtc));

        await _db.SaveChangesAsync(ct);
        return ServiceResult<Document>.Success(created);
    }

    public async Task<ServiceResult<object>> GetByIdAsync(string id, System.Security.Claims.ClaimsPrincipal user, HttpContext httpContext, CancellationToken ct = default)
    {
        var item = await _db.Documents.FindAsync([id], ct);
        if (item is null)
        {
            return ServiceResult<object>.NotFoundResult();
        }

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(item.ClientId))
        {
            return ServiceResult<object>.ForbiddenResult();
        }

        await _db.WriteAuditLogAsync(
            user,
            "documents.viewed",
            "document",
            item.Id,
            item.ClientId,
            JsonSerializer.Serialize(new { item.Id, item.CurrentVersionNumber }),
            ct);
        await _db.WriteDocumentAccessLogAsync(
            user,
            httpContext,
            item,
            "view",
            JsonSerializer.Serialize(new { item.Id, item.CurrentVersionNumber }),
            ct);

        return ServiceResult<object>.Success(await BuildDocumentResponseAsync(item, ct));
    }

    public async Task<ServiceResult<IReadOnlyList<object>>> GetVersionsAsync(string id, System.Security.Claims.ClaimsPrincipal user, HttpContext httpContext, CancellationToken ct = default)
    {
        var item = await _db.Documents.FindAsync([id], ct);
        if (item is null)
        {
            return ServiceResult<IReadOnlyList<object>>.NotFoundResult();
        }

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(item.ClientId))
        {
            return ServiceResult<IReadOnlyList<object>>.ForbiddenResult();
        }

        var versions = await _db.DocumentVersions
            .Where(x => x.DocumentId == id)
            .OrderByDescending(x => x.VersionNumber)
            .ThenByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);

        await _db.WriteDocumentAccessLogAsync(
            user,
            httpContext,
            item,
            "view_versions",
            JsonSerializer.Serialize(new { item.Id, versionCount = versions.Count }),
            ct);

        return ServiceResult<IReadOnlyList<object>>.Success(
            versions.Select(version => (object)new
            {
                version.Id,
                version.DocumentId,
                version.VersionNumber,
                version.Name,
                version.OriginalFileName,
                version.StoredFileName,
                version.FileType,
                version.SizeBytes,
                version.StorageKey,
                version.UploadedByUserId,
                version.CreatedAtUtc,
                isCurrent = version.IsCurrentVersion
            }).ToList());
    }

    public async Task<ServiceResult<(StoredFileContent Content, string FileName)>> DownloadAsync(string id, System.Security.Claims.ClaimsPrincipal user, HttpContext httpContext, CancellationToken ct = default)
    {
        var item = await _db.Documents.FindAsync([id], ct);
        if (item is null)
        {
            return ServiceResult<(StoredFileContent Content, string FileName)>.NotFoundResult();
        }

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(item.ClientId))
        {
            return ServiceResult<(StoredFileContent Content, string FileName)>.ForbiddenResult();
        }

        if (string.IsNullOrWhiteSpace(item.StorageKey))
        {
            return ServiceResult<(StoredFileContent Content, string FileName)>.NotFoundResult("Document file is not available.");
        }

        var file = await _fileStorage.OpenReadAsync(item.StorageKey, ct);
        if (file is null)
        {
            return ServiceResult<(StoredFileContent Content, string FileName)>.NotFoundResult("Document file could not be found in storage.");
        }

        await _db.WriteAuditLogAsync(
            user,
            "documents.downloaded",
            "document",
            item.Id,
            item.ClientId,
            JsonSerializer.Serialize(new { item.Id, item.CurrentVersionNumber, item.StorageKey }),
            ct);
        await _db.WriteDocumentAccessLogAsync(
            user,
            httpContext,
            item,
            "download",
            JsonSerializer.Serialize(new { item.Id, item.CurrentVersionNumber, item.StorageKey }),
            ct);

        var appContent = new StoredFileContent(file.Stream, file.ContentType);
        return ServiceResult<(StoredFileContent Content, string FileName)>.Success((appContent, item.Name));
    }

    public async Task<ServiceResult<Document>> UpdateAsync(string id, Document request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default)
    {
        var item = await _db.Documents.FindAsync([id], ct);
        if (item is null)
        {
            return ServiceResult<Document>.NotFoundResult();
        }

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(item.ClientId))
        {
            return ServiceResult<Document>.ForbiddenResult();
        }

        item.UpdateMetadata(
            request.Name,
            NormalizeCategory(request.Category),
            DocumentDomainValues.ToDocumentStatus(NormalizeDocumentStatus(request.Status)),
            request.SizeBytes,
            request.StorageKey);

        await _db.SaveChangesAsync(ct);
        return ServiceResult<Document>.Success(item);
    }

    public async Task<ServiceResult<Document>> UpdateStatusAsync(string id, UpdateDocumentStatusRequest request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default)
    {
        var document = await _db.Documents.FindAsync([id], ct);
        if (document is null)
        {
            return ServiceResult<Document>.NotFoundResult();
        }

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(document.ClientId))
        {
            return ServiceResult<Document>.ForbiddenResult();
        }

        await ApplyLifecycleDecisionAsync(document, NormalizeDocumentStatus(request.Status), null, null, user, ct);
        return ServiceResult<Document>.Success(document);
    }

    public async Task<ServiceResult<object>> ReviewAsync(string id, AddReviewDecisionRequest request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default)
    {
        DocumentValidators.ValidateReview(request);

        var document = await _db.Documents.FindAsync([id], ct);
        if (document is null)
        {
            return ServiceResult<object>.NotFoundResult();
        }

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(document.ClientId))
        {
            return ServiceResult<object>.ForbiddenResult();
        }

        var reviewDecision = await ApplyLifecycleDecisionAsync(document, request.Decision.Trim().ToLowerInvariant(), request.Reason, request.InternalNote, user, ct);
        return ServiceResult<object>.Success(new
        {
            reviewDecision.Id,
            reviewDecision.Decision,
            reviewDecision.Reason,
            reviewDecision.InternalNote,
            reviewDecision.DecidedAtUtc,
            documentId = document.Id,
            documentStatus = document.Status
        });
    }

    public async Task<ServiceResult<object>> RequestReuploadAsync(string id, RequestReuploadRequest request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default)
    {
        DocumentValidators.ValidateReupload(request);

        var document = await _db.Documents.FindAsync([id], ct);
        if (document is null)
        {
            return ServiceResult<object>.NotFoundResult();
        }

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(document.ClientId))
        {
            return ServiceResult<object>.ForbiddenResult();
        }

        var reviewDecision = await ApplyLifecycleDecisionAsync(document, "request_reupload", request.Reason, request.InternalNote, user, ct);
        return ServiceResult<object>.Success(new
        {
            reviewDecision.Id,
            reviewDecision.Decision,
            reviewDecision.Reason,
            reviewDecision.InternalNote,
            reviewDecision.DecidedAtUtc,
            documentId = document.Id,
            documentStatus = document.Status
        });
    }

    public async Task<ServiceResult<DocumentComment>> AddCommentAsync(string id, AddDocumentCommentRequest request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default)
    {
        DocumentValidators.ValidateComment(request);

        var item = await _db.Documents.FindAsync([id], ct);
        if (item is null)
        {
            return ServiceResult<DocumentComment>.NotFoundResult();
        }

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(item.ClientId))
        {
            return ServiceResult<DocumentComment>.ForbiddenResult();
        }

        var authorId = user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? "unknown";
        var authorRole = user.IsAdmin() ? "admin" : user.IsAccountant() ? "accountant" : "client";

        var comment = DocumentComment.Create(
            $"dc_{Guid.NewGuid():N}",
            item.Id,
            authorId,
            authorRole,
            request.Message);

        _db.DocumentComments.Add(comment);
        await _db.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(
            user,
            "comment.added",
            "document",
            item.Id,
            item.ClientId,
            JsonSerializer.Serialize(new { comment.Id, comment.AuthorRole, target = "document" }),
            ct);

        var recipientRole = authorRole == "client" ? "accountant" : "client";
        var recipients = await _db.ResolveNotificationRecipientsAsync(item.ClientId, recipientRole, ct);
        await _db.AddNotificationsAsync(
            user,
            recipients,
            item.ClientId,
            "document.comment",
            "Document comment added",
            $"New comment added to document '{item.Name}'.",
            $"/documents/{item.Id}",
            new { documentId = item.Id, commentId = comment.Id },
            ct);

        return ServiceResult<DocumentComment>.Success(comment);
    }

    public async Task<ServiceResult<IReadOnlyList<DocumentComment>>> GetCommentsAsync(string id, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default)
    {
        var item = await _db.Documents.FindAsync([id], ct);
        if (item is null)
        {
            return ServiceResult<IReadOnlyList<DocumentComment>>.NotFoundResult();
        }

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(item.ClientId))
        {
            return ServiceResult<IReadOnlyList<DocumentComment>>.ForbiddenResult();
        }

        var comments = await _db.DocumentComments
            .Where(x => x.DocumentId == item.Id)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(ct);

        return ServiceResult<IReadOnlyList<DocumentComment>>.Success(comments);
    }

    public async Task<ServiceResult<bool>> DeleteAsync(string id, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default)
    {
        var item = await _db.Documents.FindAsync([id], ct);
        if (item is null)
        {
            return ServiceResult<bool>.NotFoundResult();
        }

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(item.ClientId))
        {
            return ServiceResult<bool>.ForbiddenResult();
        }

        _db.Documents.Remove(item);
        await _db.SaveChangesAsync(ct);
        return ServiceResult<bool>.Success(true);
    }

    private async Task<object> BuildDocumentResponseAsync(Document item, CancellationToken ct)
    {
        DocumentSlot? slot = null;
        MonthlyPack? pack = null;

        if (!string.IsNullOrWhiteSpace(item.DocumentSlotId))
        {
            slot = await _db.DocumentSlots.FirstOrDefaultAsync(x => x.Id == item.DocumentSlotId, ct);
            if (slot is not null)
            {
                pack = await _db.MonthlyPacks.FirstOrDefaultAsync(x => x.Id == slot.MonthlyPackId, ct);
            }
        }

        return new
        {
            item.Id,
            item.ClientId,
            monthlyPackId = pack?.Id,
            documentSlotId = slot?.Id,
            documentType = item.Category,
            item.Name,
            item.Status,
            item.FileType,
            item.SizeBytes,
            item.StorageKey,
            item.UploadedByUserId,
            item.CurrentVersionNumber,
            item.UploadedAtUtc,
            item.UpdatedAtUtc
        };
    }

    private async Task<MonthlyPack?> ResolveMonthlyPackAsync(string clientId, string? monthlyPackId, string? documentSlotId, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(documentSlotId))
        {
            var slot = await _db.DocumentSlots.FirstOrDefaultAsync(x => x.Id == documentSlotId, ct);
            if (slot is not null)
            {
                return await _db.MonthlyPacks.FirstOrDefaultAsync(x => x.Id == slot.MonthlyPackId && x.ClientId == clientId, ct);
            }
        }

        if (!string.IsNullOrWhiteSpace(monthlyPackId))
        {
            return await _db.MonthlyPacks.FirstOrDefaultAsync(x => x.Id == monthlyPackId && x.ClientId == clientId, ct);
        }

        return null;
    }

    private async Task<DocumentSlot?> ResolveSlotAsync(string clientId, string monthlyPackId, string? documentSlotId, string category, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(documentSlotId))
        {
            return await _db.DocumentSlots.FirstOrDefaultAsync(x => x.Id == documentSlotId && x.ClientId == clientId && x.MonthlyPackId == monthlyPackId, ct);
        }

        return await _db.DocumentSlots.FirstOrDefaultAsync(x => x.ClientId == clientId && x.MonthlyPackId == monthlyPackId && x.Category == category, ct);
    }

    private async Task<ReviewDecision> ApplyLifecycleDecisionAsync(Document document, string decision, string? reason, string? internalNote, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct)
    {
        var reviewerUserId = user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? "unknown";
        var reviewerRole = user.IsAdmin() ? "admin" : "accountant";
        var now = DateTime.UtcNow;

        var reviewDecision = ReviewDecision.Create(
            $"rd_{Guid.NewGuid():N}",
            document.Id,
            decision,
            reviewerUserId,
            reviewerRole,
            reason,
            internalNote,
            now);

        _db.ReviewDecisions.Add(reviewDecision);

        var slot = string.IsNullOrWhiteSpace(document.DocumentSlotId)
            ? null
            : await _db.DocumentSlots.FirstOrDefaultAsync(x => x.Id == document.DocumentSlotId, ct);
        var pack = slot is null
            ? null
            : await _db.MonthlyPacks.FirstOrDefaultAsync(x => x.Id == slot.MonthlyPackId, ct);

        switch (decision)
        {
            case "under_review":
                document.MarkUnderReview();
                if (slot is not null)
                {
                    slot.MarkUnderReview();
                }
                if (pack is not null)
                {
                    pack.MarkUnderReview();
                }
                break;
            case "accepted":
                document.Accept();
                if (slot is not null)
                {
                    slot.Accept(document.Id);
                }
                break;
            case "rejected":
            case "request_reupload":
                document.Reject();
                if (slot is not null)
                {
                    slot.Reject(document.Id);
                }
                if (pack is not null)
                {
                    pack.Reopen();
                }
                break;
            default:
                throw new InvalidOperationException("Unsupported document lifecycle decision.");
        }

        if (pack is not null)
        {
            await RecalculateMonthlyPackStatusAsync(pack, ct);
        }

        await _db.SaveChangesAsync(ct);

        var action = decision switch
        {
            "under_review" => "documents.reviewed",
            "accepted" => "documents.accepted",
            "rejected" => "documents.rejected",
            "request_reupload" => "documents.reupload_requested",
            _ => "documents.reviewed"
        };

        await _db.WriteAuditLogAsync(
            user,
            action,
            "document",
            document.Id,
            document.ClientId,
            JsonSerializer.Serialize(new
            {
                document.Id,
                decision,
                reason = reviewDecision.Reason,
                internalNote = reviewDecision.InternalNote,
                document.Status
            }),
            ct);

        if (decision is "rejected" or "request_reupload")
        {
            await EnsureReuploadRequestAsync(document, reviewDecision.Reason ?? "Please re-upload a corrected document.", user, ct);
        }

        if (decision == "accepted")
        {
            var recipients = await _db.ResolveNotificationRecipientsAsync(document.ClientId, "client", ct);
            await _db.AddNotificationsAsync(
                user,
                recipients,
                document.ClientId,
                "document.approved",
                "Document approved",
                $"Document '{document.Name}' was approved.",
                $"/documents/{document.Id}",
                new { document.Id, reason = reviewDecision.Reason },
                ct);
        }

        if (decision is "rejected" or "request_reupload")
        {
            var recipients = await _db.ResolveNotificationRecipientsAsync(document.ClientId, "client", ct);
            await _db.AddNotificationsAsync(
                user,
                recipients,
                document.ClientId,
                decision == "rejected" ? "document.rejected" : "document.reupload_requested",
                decision == "rejected" ? "Document rejected" : "Re-upload requested",
                decision == "rejected"
                    ? $"Document '{document.Name}' was rejected."
                    : $"A corrected version of '{document.Name}' was requested.",
                $"/documents/{document.Id}",
                new { document.Id, reason = reviewDecision.Reason },
                ct);
        }

        return reviewDecision;
    }

    private async Task EnsureReuploadRequestAsync(Document document, string reason, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct)
    {
        var existing = await _db.Requests.FirstOrDefaultAsync(x =>
            x.ClientId == document.ClientId &&
            x.RelatedDocumentId == document.Id &&
            x.RequestType == "reupload_required" &&
            x.Status != "resolved", ct);

        if (existing is not null)
        {
            existing.UpdateDetails(
                existing.RequestType,
                existing.RelatedDocumentId,
                existing.Title,
                reason.Trim(),
                RequestDomainValues.ToRequestPriority(existing.Priority),
                existing.DueDateUtc);
            existing.MarkWaitingOnClient();
            await _db.SaveChangesAsync(ct);
            return;
        }

        var requestItem = RequestItem.Create(
            $"req_{Guid.NewGuid():N}",
            document.ClientId,
            "reupload_required",
            document.Id,
            $"Re-upload required: {document.Name}",
            reason.Trim(),
            RequestPriority.High,
            user.GetUserId() ?? "unknown",
            RequestStatus.WaitingOnClient,
            null);

        _db.Requests.Add(requestItem);
        await _db.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(
            user,
            "request.created",
            "request",
            requestItem.Id,
            requestItem.ClientId,
            JsonSerializer.Serialize(new { requestItem.RequestType, requestItem.RelatedDocumentId, requestItem.Status }),
            ct);
    }

    private async Task RecalculateMonthlyPackStatusAsync(MonthlyPack pack, CancellationToken ct)
    {
        var slots = await _db.DocumentSlots.Where(x => x.MonthlyPackId == pack.Id).ToListAsync(ct);
        if (slots.Count == 0)
        {
            pack.MarkDraft();
            return;
        }

        if (slots.Where(x => x.IsRequired).All(x => x.Status == DocumentSlotStatus.Accepted.ToStorageValue()))
        {
            pack.Complete();
        }
        else if (slots.Any(x => x.Status == DocumentSlotStatus.Rejected.ToStorageValue()))
        {
            pack.Reopen();
        }
        else if (slots.Any(x => x.Status == DocumentSlotStatus.UnderReview.ToStorageValue()))
        {
            pack.MarkUnderReview();
        }
        else if (slots.Any(x => x.Status == DocumentSlotStatus.Uploaded.ToStorageValue()))
        {
            if (pack.SubmittedAtUtc.HasValue)
            {
                pack.MarkSubmitted();
            }
            else
            {
                pack.MarkInProgress();
            }
        }
        else if (slots.Any(x => x.Status is "accepted" or "rejected" or "filed"))
        {
            pack.MarkInProgress();
        }
        else
        {
            pack.MarkDraft();
        }
    }

    private static string NormalizeCategory(string value)
    {
        return DocumentDomainValues.NormalizeCategory(value);
    }

    private static string NormalizeDocumentStatus(string rawStatus)
    {
        var normalized = string.IsNullOrWhiteSpace(rawStatus) ? "uploaded" : rawStatus.Trim().ToLowerInvariant();
        return normalized switch
        {
            "draft" => "draft",
            "pending" => "uploaded",
            "submitted" => "uploaded",
            "uploaded" => "uploaded",
            "under_review" => "under_review",
            "accepted" => "accepted",
            "rejected" => "rejected",
            "filed" => "filed",
            _ => "uploaded"
        };
    }
}



