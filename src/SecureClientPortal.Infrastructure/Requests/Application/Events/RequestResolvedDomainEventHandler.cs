using SecureClientPortal.Backend.Application.Common.Events;
using SecureClientPortal.Backend.Application.Notifications.Events;

namespace SecureClientPortal.Backend.Infrastructure.Requests.Application.Events;

public sealed class RequestResolvedDomainEventHandler : IDomainEventHandler<RequestResolvedDomainEvent>
{
    public Task<IReadOnlyCollection<IIntegrationEvent>> HandleAsync(RequestResolvedDomainEvent domainEvent, CancellationToken ct = default)
    {
        var integrationEvent = new NotificationRequestedIntegrationEvent(
            domainEvent.ResolvedByUserId,
            "accountant",
            domainEvent.ClientId,
            "client",
            "request.resolved",
            "Request resolved",
            $"Request '{domainEvent.Title}' has been resolved.",
            $"/requests/{domainEvent.RequestId}",
            new { requestId = domainEvent.RequestId, domainEvent.RequestType },
            domainEvent.OccurredAtUtc);

        return Task.FromResult<IReadOnlyCollection<IIntegrationEvent>>([integrationEvent]);
    }
}
