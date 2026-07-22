using SecureClientPortal.Backend.Models;

namespace SecureClientPortal.Backend.Domain.Modules.Documents.Events;

public sealed record DocumentReviewedDomainEvent(
    Guid DocumentId,
    Guid ClientId,
    string DocumentName,
    string Decision,
    string? Reason,
    Guid ReviewerUserId,
    string ReviewerRole,
    DateTime OccurredAtUtc) : IDomainEvent;
