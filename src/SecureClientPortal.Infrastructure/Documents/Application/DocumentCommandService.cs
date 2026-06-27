using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Documents;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using System.Text.Json;
using StorageFileStorage = SecureClientPortal.Backend.Storage.IFileStorage;

namespace SecureClientPortal.Backend.Infrastructure.Documents.Application;

public sealed class DocumentCommandService : IDocumentCommandService
{
    private readonly IDocumentModuleDbContext _documents;
    private readonly PortalDbContext _db;
    private readonly StorageFileStorage _fileStorage;

    public DocumentCommandService(IDocumentModuleDbContext documents, PortalDbContext db, StorageFileStorage fileStorage)
    {
        _documents = documents;
        _db = db;
        _fileStorage = fileStorage;
    }

    public async Task<ServiceResult<FilingRule>> UpdateFilingRuleAsync(string category, FilingRuleUpdateRequest request, CancellationToken ct = default)
    {
        var normalizedCategory = DocumentDomainValues.NormalizeCategory(category);
        var item = await _documents.FilingRules.FirstOrDefaultAsync(x => x.Category == normalizedCategory, ct);
        if (item is null)
        {
            return ServiceResult<FilingRule>.NotFoundResult();
        }

        item.Update(item.Category, item.Description, request.IsEnabled);
        await _documents.SaveChangesAsync(ct);
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

        var normalizedCategory = DocumentDomainValues.NormalizeCategory(request.DocumentType);
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

        var stored = await _fileStorage.SaveAsync(request.File, request.ClientId.ToString(), ct);
        var actorUserId = user.GetUserId() ?? throw new InvalidOperationException("Authenticated user id is required.");
        var now = DateTime.UtcNow;

        Document document;
        var isNewDocument = !request.DocumentId.HasValue;
        if (isNewDocument)
        {
            document = Document.CreateUploaded(
                Guid.NewGuid(),
                request.ClientId,
                pack.Id,
                stored.OriginalFileName,
                normalizedCategory,
                slot.Id,
                stored.ContentType,
                stored.SizeBytes,
                stored.StorageKey,
                actorUserId);
            _documents.Documents.Add(document);
        }
        else
        {
            var existingDocument = await _documents.Documents.FirstOrDefaultAsync(x => x.Id == request.DocumentId!.Value, ct);
            if (existingDocument is null)
            {
                return ServiceResult<object>.NotFoundResult("Requested document could not be found.");
            }

            document = existingDocument;
            if (document.ClientId != request.ClientId)
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

            var previousVersions = await _documents.DocumentVersions
                .Where(x => x.DocumentId == document.Id && x.IsCurrentVersion)
                .ToListAsync(ct);
            foreach (var previousVersion in previousVersions)
            {
                previousVersion.MarkNotCurrent();
            }
        }

        _documents.DocumentVersions.Add(DocumentVersion.Create(
            Guid.NewGuid(),
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
        if (isNewDocument) pack.MarkInProgress(); else pack.Reopen();

        await _documents.SaveChangesAsync(ct);
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
            Guid.NewGuid(),
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

        return ServiceResult<object>.Success(new
        {
            document.Id,
            document.ClientId,
            monthlyPackId = pack.Id,
            documentSlotId = slot.Id,
            documentType = document.Category,
            document.Name,
            document.Status,
            document.FileType,
            document.SizeBytes,
            document.StorageKey,
            document.UploadedByUserId,
            document.CurrentVersionNumber,
            document.UploadedAtUtc,
            document.UpdatedAtUtc
        });
    }

    public async Task<ServiceResult<Document>> CreateAsync(CreateDocumentRequest request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default)
    {
        DocumentValidators.ValidateCreate(request);

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(request.ClientId))
        {
            return ServiceResult<Document>.ForbiddenResult();
        }

        var pack = await _documents.MonthlyPacks.FirstOrDefaultAsync(x => x.Id == request.MonthlyPackId && x.ClientId == request.ClientId, ct);
        if (pack is null)
        {
            return ServiceResult<Document>.ErrorResult("Monthly pack was not found for the selected client.");
        }

        DocumentSlot? slot = null;
        if (request.DocumentSlotId.HasValue)
        {
            slot = await _documents.DocumentSlots.FirstOrDefaultAsync(x =>
                x.Id == request.DocumentSlotId.Value &&
                x.ClientId == request.ClientId &&
                x.MonthlyPackId == request.MonthlyPackId, ct);

            if (slot is null)
            {
                return ServiceResult<Document>.ErrorResult("Document slot was not found for the selected client monthly pack.");
            }
        }

        var created = Document.CreateUploaded(
            Guid.NewGuid(),
            request.ClientId,
            request.MonthlyPackId,
            request.Name,
            DocumentDomainValues.NormalizeCategory(request.Category),
            request.DocumentSlotId,
            request.FileType,
            request.SizeBytes,
            request.StorageKey,
            request.UploadedByUserId);

        switch (NormalizeDocumentStatus(request.Status))
        {
            case "under_review":
                created.MarkUnderReview();
                slot?.MarkUnderReview();
                pack.MarkUnderReview();
                break;
            case "accepted":
                created.Accept();
                slot?.Accept(created.Id);
                break;
            case "rejected":
                created.Reject();
                slot?.Reject(created.Id);
                pack.Reopen();
                break;
            case "filed":
                created.File(request.UploadedByUserId);
                break;
        }

        _documents.Documents.Add(created);
        _documents.DocumentVersions.Add(DocumentVersion.Create(
            Guid.NewGuid(),
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

        if (slot is not null)
        {
            MonthlyPackStatusPolicy.Recalculate(pack, await _documents.DocumentSlots.Where(x => x.MonthlyPackId == pack.Id).ToListAsync(ct));
        }

        await _documents.SaveChangesAsync(ct);
        return ServiceResult<Document>.Success(created);
    }

    public async Task<ServiceResult<Document>> UpdateAsync(string id, UpdateDocumentRequest request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default)
    {
        if (!Guid.TryParse(id, out var documentId))
        {
            return ServiceResult<Document>.NotFoundResult();
        }

        var item = await _documents.Documents.FindAsync([documentId], ct);
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
            DocumentDomainValues.NormalizeCategory(request.Category),
            DocumentDomainValues.ToDocumentStatus(NormalizeDocumentStatus(request.Status)),
            request.SizeBytes,
            request.StorageKey);

        await _documents.SaveChangesAsync(ct);
        return ServiceResult<Document>.Success(item);
    }

    public async Task<ServiceResult<DocumentComment>> AddCommentAsync(string id, AddDocumentCommentRequest request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default)
    {
        DocumentValidators.ValidateComment(request);

        if (!Guid.TryParse(id, out var documentId))
        {
            return ServiceResult<DocumentComment>.NotFoundResult();
        }

        var item = await _documents.Documents.FindAsync([documentId], ct);
        if (item is null)
        {
            return ServiceResult<DocumentComment>.NotFoundResult();
        }

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(item.ClientId))
        {
            return ServiceResult<DocumentComment>.ForbiddenResult();
        }

        var authorId = user.GetUserId() ?? throw new InvalidOperationException("Authenticated user id is required.");
        var authorRole = user.IsAdmin() ? "admin" : user.IsAccountant() ? "accountant" : "client";

        var comment = DocumentComment.Create(Guid.NewGuid(), item.Id, authorId, authorRole, request.Message);
        _documents.DocumentComments.Add(comment);
        await _documents.SaveChangesAsync(ct);

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

    public async Task<ServiceResult<bool>> DeleteAsync(string id, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default)
    {
        if (!Guid.TryParse(id, out var documentId))
        {
            return ServiceResult<bool>.NotFoundResult();
        }

        var item = await _documents.Documents.FindAsync([documentId], ct);
        if (item is null)
        {
            return ServiceResult<bool>.NotFoundResult();
        }

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(item.ClientId))
        {
            return ServiceResult<bool>.ForbiddenResult();
        }

        _documents.Documents.Remove(item);
        await _documents.SaveChangesAsync(ct);
        return ServiceResult<bool>.Success(true);
    }

    private async Task<MonthlyPack?> ResolveMonthlyPackAsync(Guid clientId, Guid? monthlyPackId, Guid? documentSlotId, CancellationToken ct)
    {
        if (documentSlotId.HasValue)
        {
            var slot = await _documents.DocumentSlots.FirstOrDefaultAsync(x => x.Id == documentSlotId.Value, ct);
            if (slot is not null)
            {
                return await _documents.MonthlyPacks.FirstOrDefaultAsync(x => x.Id == slot.MonthlyPackId && x.ClientId == clientId, ct);
            }
        }

        if (monthlyPackId.HasValue)
        {
            return await _documents.MonthlyPacks.FirstOrDefaultAsync(x => x.Id == monthlyPackId.Value && x.ClientId == clientId, ct);
        }

        return null;
    }

    private async Task<DocumentSlot?> ResolveSlotAsync(Guid clientId, Guid monthlyPackId, Guid? documentSlotId, string category, CancellationToken ct)
    {
        if (documentSlotId.HasValue)
        {
            return await _documents.DocumentSlots.FirstOrDefaultAsync(x => x.Id == documentSlotId.Value && x.ClientId == clientId && x.MonthlyPackId == monthlyPackId, ct);
        }

        return await _documents.DocumentSlots.FirstOrDefaultAsync(x => x.ClientId == clientId && x.MonthlyPackId == monthlyPackId && x.Category == category, ct);
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
