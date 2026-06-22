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

        item.IsEnabled = request.IsEnabled;
        item.UpdatedAtUtc = DateTime.UtcNow;
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
            document = new Document
            {
                Id = $"doc_{Guid.NewGuid():N}",
                ClientId = request.ClientId,
                MonthlyPackId = pack.Id,
                Name = stored.OriginalFileName,
                Category = normalizedCategory,
                DocumentSlotId = slot.Id,
                Status = "uploaded",
                FileType = stored.ContentType,
                SizeBytes = stored.SizeBytes,
                StorageKey = stored.StorageKey,
                UploadedByUserId = actorUserId,
                CurrentVersionNumber = 1,
                UploadedAtUtc = now,
                UpdatedAtUtc = now
            };
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

            document.Name = stored.OriginalFileName;
            document.MonthlyPackId = pack.Id;
            document.Category = normalizedCategory;
            document.DocumentSlotId = slot.Id;
            document.FileType = stored.ContentType;
            document.SizeBytes = stored.SizeBytes;
            document.StorageKey = stored.StorageKey;
            document.UploadedByUserId = actorUserId;
            document.Status = "uploaded";
            document.CurrentVersionNumber += 1;
            document.IsFiled = false;
            document.FiledAtUtc = null;
            document.FiledByUserId = null;
            document.UpdatedAtUtc = now;
            document.UploadedAtUtc = now;

            var previousVersions = await _db.DocumentVersions
                .Where(x => x.DocumentId == document.Id && x.IsCurrentVersion)
                .ToListAsync(ct);
            foreach (var previousVersion in previousVersions)
            {
                previousVersion.IsCurrentVersion = false;
            }
        }

        _db.DocumentVersions.Add(new DocumentVersion
        {
            Id = $"dv_{Guid.NewGuid():N}",
            DocumentId = document.Id,
            VersionNumber = document.CurrentVersionNumber,
            Name = stored.OriginalFileName,
            OriginalFileName = stored.OriginalFileName,
            StoredFileName = stored.StoredFileName,
            FileType = stored.ContentType,
            SizeBytes = stored.SizeBytes,
            StorageKey = stored.StorageKey,
            IsCurrentVersion = true,
            UploadedByUserId = actorUserId,
            CreatedAtUtc = now
        });

        slot.CurrentDocumentId = document.Id;
        slot.Status = "uploaded";
        slot.UpdatedAtUtc = now;

        pack.Status = isNewDocument ? "in_progress" : "reopened";
        pack.SubmittedAtUtc = null;
        pack.UpdatedAtUtc = now;

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

        if (string.IsNullOrWhiteSpace(request.Id)) request.Id = $"doc_{Guid.NewGuid():N}";
        request.Category = NormalizeCategory(request.Category);
        request.Status = NormalizeDocumentStatus(request.Status);
        request.UploadedAtUtc = DateTime.UtcNow;
        request.UpdatedAtUtc = request.UploadedAtUtc;
        request.CurrentVersionNumber = 1;

        _db.Documents.Add(request);
        _db.DocumentVersions.Add(new DocumentVersion
        {
            Id = $"dv_{Guid.NewGuid():N}",
            DocumentId = request.Id,
            VersionNumber = 1,
            Name = request.Name,
            OriginalFileName = request.Name,
            StoredFileName = Path.GetFileName(request.StorageKey ?? request.Name),
            FileType = request.FileType,
            SizeBytes = request.SizeBytes,
            StorageKey = request.StorageKey,
            IsCurrentVersion = true,
            UploadedByUserId = request.UploadedByUserId,
            CreatedAtUtc = request.UploadedAtUtc
        });

        await _db.SaveChangesAsync(ct);
        return ServiceResult<Document>.Success(request);
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

        item.Name = request.Name;
        item.Category = NormalizeCategory(request.Category);
        item.Status = NormalizeDocumentStatus(request.Status);
        item.SizeBytes = request.SizeBytes;
        item.StorageKey = request.StorageKey;
        item.UpdatedAtUtc = DateTime.UtcNow;

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

        var comment = new DocumentComment
        {
            Id = $"dc_{Guid.NewGuid():N}",
            DocumentId = item.Id,
            AuthorUserId = authorId,
            AuthorRole = authorRole,
            Message = request.Message.Trim(),
            CreatedAtUtc = DateTime.UtcNow,
        };

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

        var reviewDecision = new ReviewDecision
        {
            Id = $"rd_{Guid.NewGuid():N}",
            DocumentId = document.Id,
            Decision = decision,
            ReviewerUserId = reviewerUserId,
            ReviewerRole = reviewerRole,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            InternalNote = string.IsNullOrWhiteSpace(internalNote) ? null : internalNote.Trim(),
            DecidedAtUtc = now
        };

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
                document.Status = "under_review";
                if (slot is not null)
                {
                    slot.Status = "under_review";
                    slot.UpdatedAtUtc = now;
                }
                if (pack is not null)
                {
                    pack.Status = "under_review";
                    pack.SubmittedAtUtc ??= now;
                    pack.UpdatedAtUtc = now;
                }
                break;
            case "accepted":
                document.Status = "accepted";
                document.IsFiled = false;
                document.FiledAtUtc = null;
                document.FiledByUserId = null;
                if (slot is not null)
                {
                    slot.Status = "accepted";
                    slot.CurrentDocumentId = document.Id;
                    slot.UpdatedAtUtc = now;
                }
                break;
            case "rejected":
            case "request_reupload":
                document.Status = "rejected";
                document.IsFiled = false;
                document.FiledAtUtc = null;
                document.FiledByUserId = null;
                if (slot is not null)
                {
                    slot.Status = "rejected";
                    slot.CurrentDocumentId = document.Id;
                    slot.UpdatedAtUtc = now;
                }
                if (pack is not null)
                {
                    pack.Status = "reopened";
                    pack.SubmittedAtUtc = null;
                    pack.UpdatedAtUtc = now;
                }
                break;
            default:
                throw new InvalidOperationException("Unsupported document lifecycle decision.");
        }

        document.UpdatedAtUtc = now;

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
            existing.Status = "waiting_on_client";
            existing.Description = reason.Trim();
            existing.UpdatedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return;
        }

        var requestItem = new RequestItem
        {
            Id = $"req_{Guid.NewGuid():N}",
            ClientId = document.ClientId,
            RequestType = "reupload_required",
            RelatedDocumentId = document.Id,
            Title = $"Re-upload required: {document.Name}",
            Description = reason.Trim(),
            Priority = "high",
            Status = "waiting_on_client",
            RequestedByUserId = user.GetUserId() ?? "unknown",
            RequestedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

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
            pack.Status = "draft";
            pack.UpdatedAtUtc = DateTime.UtcNow;
            return;
        }

        if (slots.Where(x => x.IsRequired).All(x => x.Status == "accepted"))
        {
            pack.Status = "completed";
        }
        else if (slots.Any(x => x.Status == "rejected"))
        {
            pack.Status = "reopened";
            pack.SubmittedAtUtc = null;
        }
        else if (slots.Any(x => x.Status == "under_review"))
        {
            pack.Status = "under_review";
        }
        else if (slots.Any(x => x.Status == "uploaded"))
        {
            pack.Status = pack.SubmittedAtUtc.HasValue ? "submitted" : "in_progress";
        }
        else if (slots.Any(x => x.Status is "accepted" or "rejected" or "filed"))
        {
            pack.Status = "in_progress";
        }
        else
        {
            pack.Status = "draft";
        }

        pack.UpdatedAtUtc = DateTime.UtcNow;
    }

    private static string NormalizeCategory(string value)
    {
        var raw = value.Trim().ToLowerInvariant().Replace("-", "_").Replace(" ", "_");
        return raw switch
        {
            "bankstatement" => "bank_statement",
            "bank_statement" => "bank_statement",
            "invoice" => "invoices",
            "invoices" => "invoices",
            "signeddocuments" => "signed_documents",
            "signed_documents" => "signed_documents",
            "compliancerecord" => "compliance_record",
            "compliance_record" => "compliance_record",
            "payrollsummary" => "payroll_summary",
            "payroll_summary" => "payroll_summary",
            "taxworkingpapers" => "tax_working_papers",
            "tax_working_papers" => "tax_working_papers",
            "proofofpayment" => "proof_of_payment",
            "proof_of_payment" => "proof_of_payment",
            "creditnotes" => "credit_notes",
            "credit_notes" => "credit_notes",
            "debitnotes" => "debit_notes",
            "debit_notes" => "debit_notes",
            _ => raw
        };
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
