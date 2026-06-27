using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Documents;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using System.Text.Json;
using StorageFileStorage = SecureClientPortal.Backend.Storage.IFileStorage;

namespace SecureClientPortal.Backend.Infrastructure.Documents.Application;

public sealed class DocumentQueryService : IDocumentQueryService
{
    private readonly IDocumentModuleDbContext _documents;
    private readonly PortalDbContext _db;
    private readonly StorageFileStorage _fileStorage;

    public DocumentQueryService(IDocumentModuleDbContext documents, PortalDbContext db, StorageFileStorage fileStorage)
    {
        _documents = documents;
        _db = db;
        _fileStorage = fileStorage;
    }

    public async Task<IReadOnlyList<Document>> GetAllAsync(System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default)
    {
        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        return await _documents.Documents
            .Where(x => allowedClientIds.Contains(x.ClientId))
            .OrderByDescending(x => x.UploadedAtUtc)
            .ToListAsync(ct);
    }

    public async Task<ServiceResult<IReadOnlyList<Document>>> GetFilingRegisterAsync(System.Security.Claims.ClaimsPrincipal user, string? clientId = null, CancellationToken ct = default)
    {
        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        var query = _documents.Documents.Where(x => x.IsFiled && allowedClientIds.Contains(x.ClientId));
        if (Guid.TryParse(clientId, out var parsedClientId))
        {
            if (!allowedClientIds.Contains(parsedClientId))
            {
                return ServiceResult<IReadOnlyList<Document>>.ForbiddenResult();
            }

            query = query.Where(x => x.ClientId == parsedClientId);
        }

        return ServiceResult<IReadOnlyList<Document>>.Success(
            await query.OrderByDescending(x => x.FiledAtUtc).ThenByDescending(x => x.UploadedAtUtc).ToListAsync(ct));
    }

    public async Task<IReadOnlyList<FilingRule>> GetFilingRulesAsync(CancellationToken ct = default)
    {
        return await _documents.FilingRules.OrderBy(x => x.Category).ToListAsync(ct);
    }

    public async Task<ServiceResult<object>> GetByIdAsync(string id, System.Security.Claims.ClaimsPrincipal user, HttpContext httpContext, CancellationToken ct = default)
    {
        if (!Guid.TryParse(id, out var documentId))
        {
            return ServiceResult<object>.NotFoundResult();
        }

        var item = await _documents.Documents.FindAsync([documentId], ct);
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
        if (!Guid.TryParse(id, out var documentId))
        {
            return ServiceResult<IReadOnlyList<object>>.NotFoundResult();
        }

        var item = await _documents.Documents.FindAsync([documentId], ct);
        if (item is null)
        {
            return ServiceResult<IReadOnlyList<object>>.NotFoundResult();
        }

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(item.ClientId))
        {
            return ServiceResult<IReadOnlyList<object>>.ForbiddenResult();
        }

        var versions = await _documents.DocumentVersions
            .Where(x => x.DocumentId == documentId)
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
        if (!Guid.TryParse(id, out var documentId))
        {
            return ServiceResult<(StoredFileContent Content, string FileName)>.NotFoundResult();
        }

        var item = await _documents.Documents.FindAsync([documentId], ct);
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

        return ServiceResult<(StoredFileContent Content, string FileName)>.Success((new StoredFileContent(file.Stream, file.ContentType), item.Name));
    }

    public async Task<ServiceResult<IReadOnlyList<DocumentComment>>> GetCommentsAsync(string id, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default)
    {
        if (!Guid.TryParse(id, out var documentId))
        {
            return ServiceResult<IReadOnlyList<DocumentComment>>.NotFoundResult();
        }

        var item = await _documents.Documents.FindAsync([documentId], ct);
        if (item is null)
        {
            return ServiceResult<IReadOnlyList<DocumentComment>>.NotFoundResult();
        }

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(item.ClientId))
        {
            return ServiceResult<IReadOnlyList<DocumentComment>>.ForbiddenResult();
        }

        var comments = await _documents.DocumentComments
            .Where(x => x.DocumentId == item.Id)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(ct);

        return ServiceResult<IReadOnlyList<DocumentComment>>.Success(comments);
    }

    private async Task<object> BuildDocumentResponseAsync(Document item, CancellationToken ct)
    {
        DocumentSlot? slot = null;
        MonthlyPack? pack = null;

        if (item.DocumentSlotId.HasValue)
        {
            slot = await _documents.DocumentSlots.FirstOrDefaultAsync(x => x.Id == item.DocumentSlotId, ct);
            if (slot is not null)
            {
                pack = await _documents.MonthlyPacks.FirstOrDefaultAsync(x => x.Id == slot.MonthlyPackId, ct);
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
}
