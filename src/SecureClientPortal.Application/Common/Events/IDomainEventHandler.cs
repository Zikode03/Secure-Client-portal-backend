using SecureClientPortal.Backend.Models;

namespace SecureClientPortal.Backend.Application.Common.Events;

public interface IDomainEventHandler<in TDomainEvent> where TDomainEvent : IDomainEvent
{
    Task<IReadOnlyCollection<IIntegrationEvent>> HandleAsync(TDomainEvent domainEvent, CancellationToken ct = default);
}
