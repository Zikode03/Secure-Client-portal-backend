using Microsoft.AspNetCore.Http;

namespace SecureClientPortal.Backend.Application.Contracts.Modules.Requests;

public record CreateRequestRequest(
    Guid ClientId,
    string RequestType,
    string Title,
    string Description,
    string Priority,
    DateTime? DueDateUtc,
    Guid? RelatedDocumentId);

public record UpdateRequestRequest(
    string RequestType,
    string Title,
    string Description,
    string Priority,
    DateTime? DueDateUtc,
    Guid? RelatedDocumentId,
    string Status);

public record AddRequestCommentRequest(string Message);
public record UpdateRequestStatusRequest(string Status);
public record ResolveRequestRequest(string? ResolutionNote);

public class UploadRequestDocumentRequest
{
    public IFormFile File { get; set; } = default!;
    public string? Message { get; set; }
}

public record RequestWorkspaceCommentResponse(
    Guid Id,
    Guid RequestId,
    Guid ClientId,
    Guid AuthorUserId,
    string AuthorRole,
    string Message,
    DateTime CreatedAtUtc);

public record RequestWorkspaceDocumentVersionResponse(
    Guid Id,
    Guid DocumentId,
    int VersionNumber,
    string Name,
    string OriginalFileName,
    string StoredFileName,
    string FileType,
    long SizeBytes,
    bool IsCurrentVersion,
    DateTime CreatedAtUtc);

public record RequestWorkspaceRelatedDocumentResponse(
    Guid Id,
    Guid ClientId,
    Guid MonthlyPackId,
    Guid? DocumentSlotId,
    string Name,
    string Category,
    string Status,
    string FileType,
    long SizeBytes,
    int CurrentVersionNumber,
    DateTime UploadedAtUtc,
    DateTime? UpdatedAtUtc,
    string DownloadUrl,
    IReadOnlyList<RequestWorkspaceDocumentVersionResponse> Versions);

public record RequestWorkspaceItemResponse(
    Guid Id,
    Guid ClientId,
    string RequestType,
    Guid? RelatedDocumentId,
    string Title,
    string Description,
    string Priority,
    string Status,
    DateTime? DueDateUtc,
    Guid RequestedByUserId,
    Guid? ResolvedByUserId,
    DateTime RequestedAtUtc,
    DateTime? ResolvedAtUtc,
    DateTime UpdatedAtUtc);

public record RequestWorkspaceResponse(
    RequestWorkspaceItemResponse Request,
    IReadOnlyList<RequestWorkspaceCommentResponse> Comments,
    RequestWorkspaceRelatedDocumentResponse? RelatedDocument,
    bool CanUploadCorrection);

public record RequestDocumentUploadResponse(
    RequestWorkspaceResponse Workspace,
    string Message);
