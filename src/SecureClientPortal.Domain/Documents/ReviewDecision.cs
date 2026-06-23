namespace SecureClientPortal.Backend.Models;

public class ReviewDecision
{
    public Guid Id { get; private set; }
    public Guid DocumentId { get; private set; }
    public string Decision { get; private set; } = string.Empty;
    public Guid ReviewerUserId { get; private set; }
    public string ReviewerRole { get; private set; } = string.Empty;
    public string? Reason { get; private set; }
    public string? InternalNote { get; private set; }
    public DateTime DecidedAtUtc { get; private set; } = DateTime.UtcNow;

    public static ReviewDecision Create(
        Guid id,
        Guid documentId,
        string decision,
        Guid reviewerUserId,
        string reviewerRole,
        string? reason,
        string? internalNote,
        DateTime? decidedAtUtc = null)
    {
        return new ReviewDecision
        {
            Id = id,
            DocumentId = documentId == Guid.Empty ? throw new ArgumentException("Document id is required.", nameof(documentId)) : documentId,
            Decision = string.IsNullOrWhiteSpace(decision) ? throw new ArgumentException("Decision is required.", nameof(decision)) : decision.Trim().ToLowerInvariant(),
            ReviewerUserId = reviewerUserId == Guid.Empty ? throw new ArgumentException("Reviewer user id is required.", nameof(reviewerUserId)) : reviewerUserId,
            ReviewerRole = string.IsNullOrWhiteSpace(reviewerRole) ? "unknown" : reviewerRole.Trim().ToLowerInvariant(),
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            InternalNote = string.IsNullOrWhiteSpace(internalNote) ? null : internalNote.Trim(),
            DecidedAtUtc = decidedAtUtc ?? DateTime.UtcNow
        };
    }
}






