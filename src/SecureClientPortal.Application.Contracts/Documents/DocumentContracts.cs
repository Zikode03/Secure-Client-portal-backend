using Microsoft.AspNetCore.Http;

namespace SecureClientPortal.Backend.Application.Contracts;

public record UpdateDocumentStatusRequest(string Status);
public record FilingRuleUpdateRequest(bool IsEnabled);
public record AddDocumentCommentRequest(string Message);
public record AddReviewDecisionRequest(string Decision, string? Reason, string? InternalNote);
public record RequestReuploadRequest(string Reason, string? InternalNote);
public class UploadDocumentRequest
{
    public Guid ClientId { get; set; }
    public Guid? MonthlyPackId { get; set; }
    public Guid? DocumentSlotId { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public Guid? DocumentId { get; set; }
    public IFormFile File { get; set; } = default!;
}
