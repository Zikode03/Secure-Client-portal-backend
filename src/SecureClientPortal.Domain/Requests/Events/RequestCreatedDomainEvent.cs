namespace SecureClientPortal.Backend.Models;

public sealed record RequestCreatedDomainEvent(
    Guid RequestId,
    Guid ClientId,
    Guid? RelatedDocumentId,
    string RequestType,
    string Title,
    string Priority,
    WorkflowActorContext Actor,
    DateTime OccurredAtUtc) : IDomainEvent;
