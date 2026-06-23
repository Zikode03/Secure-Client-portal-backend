namespace SecureClientPortal.Backend.Models;

public class ReviewDecision
{
    public string Id { get; private set; } = string.Empty;
    public string DocumentId { get; private set; } = string.Empty;
    public string Decision { get; private set; } = string.Empty;
    public string ReviewerUserId { get; private set; } = string.Empty;
    public string ReviewerRole { get; private set; } = string.Empty;
    public string? Reason { get; private set; }
    public string? InternalNote { get; private set; }
    public DateTime DecidedAtUtc { get; private set; } = DateTime.UtcNow;

    public static ReviewDecision Create(
        string id,
        string documentId,
        string decision,
        string reviewerUserId,
        string reviewerRole,
        string? reason,
        string? internalNote,
        DateTime? decidedAtUtc = null)
    {
        return new ReviewDecision
        {
            Id = id,
            DocumentId = string.IsNullOrWhiteSpace(documentId) ? throw new ArgumentException("Document id is required.", nameof(documentId)) : documentId.Trim(),
            Decision = string.IsNullOrWhiteSpace(decision) ? throw new ArgumentException("Decision is required.", nameof(decision)) : decision.Trim().ToLowerInvariant(),
            ReviewerUserId = string.IsNullOrWhiteSpace(reviewerUserId) ? throw new ArgumentException("Reviewer user id is required.", nameof(reviewerUserId)) : reviewerUserId.Trim(),
            ReviewerRole = string.IsNullOrWhiteSpace(reviewerRole) ? "unknown" : reviewerRole.Trim().ToLowerInvariant(),
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            InternalNote = string.IsNullOrWhiteSpace(internalNote) ? null : internalNote.Trim(),
            DecidedAtUtc = decidedAtUtc ?? DateTime.UtcNow
        };
    }
}
