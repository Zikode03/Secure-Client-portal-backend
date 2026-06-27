namespace SecureClientPortal.Backend.Models;

public sealed record DocumentReviewedDomainEvent(
    Guid DocumentId,
    Guid ClientId,
    string DocumentName,
    string Decision,
    string? Reason,
    Guid ReviewerUserId,
    string ReviewerRole,
    DateTime OccurredAtUtc) : IDomainEvent;
