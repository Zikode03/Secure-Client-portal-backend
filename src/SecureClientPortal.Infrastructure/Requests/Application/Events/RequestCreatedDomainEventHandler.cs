using SecureClientPortal.Backend.Application.Common.Events;
using SecureClientPortal.Backend.Application.Notifications.Events;

namespace SecureClientPortal.Backend.Infrastructure.Requests.Application.Events;

public sealed class RequestCreatedDomainEventHandler : IDomainEventHandler<RequestCreatedDomainEvent>
{
    public Task<IReadOnlyCollection<IIntegrationEvent>> HandleAsync(RequestCreatedDomainEvent domainEvent, CancellationToken ct = default)
    {
        var audienceRole = domainEvent.Actor.IsClient ? "accountant" : "client";

        var integrationEvent = new NotificationRequestedIntegrationEvent(
            domainEvent.Actor.UserId,
            domainEvent.Actor.RoleScope,
            domainEvent.ClientId,
            audienceRole,
            "request.created",
            "New workflow request",
            $"A {domainEvent.RequestType.Replace('_', ' ')} request was created for '{domainEvent.Title}'.",
            $"/requests/{domainEvent.RequestId}",
            new { requestId = domainEvent.RequestId, domainEvent.RequestType, domainEvent.Priority, domainEvent.RelatedDocumentId },
            domainEvent.OccurredAtUtc);

        return Task.FromResult<IReadOnlyCollection<IIntegrationEvent>>([integrationEvent]);
    }
}
