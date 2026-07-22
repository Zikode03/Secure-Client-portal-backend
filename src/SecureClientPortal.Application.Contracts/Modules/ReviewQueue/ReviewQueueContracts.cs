namespace SecureClientPortal.Backend.Application.Contracts.Modules.ReviewQueue;

public record ReviewQueueFilterRequest(
    Guid? ClientId,
    string? DocumentCategory,
    string? SlotStatus,
    int? MinAgeDays,
    string? Priority,
    string? Sort = "newest");

public record ReviewQueueItemResponse(
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
    string ReviewPriority,
    int ReviewAgeDays,
    int CurrentVersionNumber,
    DateTime UploadedAtUtc,
    DateTime? SubmittedAtUtc,
    string? RejectionReason);

public record ReviewQueueVersionResponse(
    Guid Id,
    Guid DocumentId,
    int VersionNumber,
    string Name,
    string OriginalFileName,
    string StoredFileName,
    string FileType,
    long SizeBytes,
    bool IsCurrent,
    Guid UploadedByUserId,
    DateTime CreatedAtUtc);

public record ReviewQueueCommentResponse(
    Guid Id,
    Guid DocumentId,
    Guid AuthorUserId,
    string AuthorRole,
    string Message,
    DateTime CreatedAtUtc);

public record ReviewQueueDecisionResponse(
    Guid Id,
    Guid DocumentId,
    string Decision,
    Guid ReviewerUserId,
    string ReviewerRole,
    string? Reason,
    string? InternalNote,
    DateTime DecidedAtUtc);

public record ReviewQueueWorkspaceResponse(
    ReviewQueueItemResponse Item,
    string DownloadUrl,
    IReadOnlyList<ReviewQueueVersionResponse> Versions,
    IReadOnlyList<ReviewQueueCommentResponse> Comments,
    IReadOnlyList<ReviewQueueDecisionResponse> ReviewHistory);
