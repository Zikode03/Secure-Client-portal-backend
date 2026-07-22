namespace SecureClientPortal.Backend.Domain.Modules.Requests.Events;

using SecureClientPortal.Backend.Models;

public sealed record RequestResolvedDomainEvent(
    Guid RequestId,
    Guid ClientId,
    string Title,
    string RequestType,
    Guid ResolvedByUserId,
    DateTime OccurredAtUtc) : IDomainEvent;
