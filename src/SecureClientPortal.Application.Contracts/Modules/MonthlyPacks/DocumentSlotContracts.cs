using Microsoft.AspNetCore.Http;
using SecureClientPortal.Backend.Application.Contracts.Modules.ReviewQueue;

namespace SecureClientPortal.Backend.Application.Contracts.Modules.MonthlyPacks;

public record CreateDocumentSlotRequest(
    Guid MonthlyPackId,
    string Category,
    string Label,
    bool IsRequired,
    DateTime? DueDateUtc);

public class UploadDocumentSlotRequest
{
    public IFormFile File { get; set; } = default!;
}

public record StartDocumentSlotReviewRequest(string? InternalNote);
public record ApproveDocumentSlotRequest(string? InternalNote);
public record RejectDocumentSlotRequest(string Reason, string? InternalNote);
public record RequestDocumentSlotReuploadRequest(string Reason, string? InternalNote);

public record DocumentSlotResponse(
    Guid Id,
    Guid MonthlyPackId,
    Guid ClientId,
    string Category,
    string Label,
    bool IsRequired,
    string Status,
    bool CanSubmit,
    Guid? CurrentDocumentId,
    DateTime? DueDateUtc,
    DateTime? SubmittedAtUtc,
    Guid? SubmittedByUserId,
    string ReviewStatus,
    string? RejectionReason,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public record DocumentSlotWorkspaceResponse(
    DocumentSlotResponse Slot,
    ReviewQueueWorkspaceResponse Workspace);
