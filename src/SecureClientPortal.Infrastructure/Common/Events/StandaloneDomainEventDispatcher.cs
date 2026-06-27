using SecureClientPortal.Backend.Application.Common.Events;

namespace SecureClientPortal.Backend.Infrastructure.Common.Events;

public sealed class StandaloneDomainEventDispatcher : IDomainEventDispatcher
{
    private readonly IReadOnlyCollection<object> _handlers;
    private readonly IIntegrationEventDispatcher _integrationEventDispatcher;

    public StandaloneDomainEventDispatcher(IEnumerable<object> handlers, IIntegrationEventDispatcher integrationEventDispatcher)
    {
        _handlers = handlers.ToArray();
        _integrationEventDispatcher = integrationEventDispatcher;
    }

    public async Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken ct = default)
    {
        foreach (var domainEvent in domainEvents)
        {
            foreach (var handler in _handlers.Where(x => SupportsDomainEvent(x, domainEvent.GetType())))
            {
                var integrationEvents = await ((dynamic)handler).HandleAsync((dynamic)domainEvent, ct);
                await _integrationEventDispatcher.DispatchAsync(integrationEvents, ct);
            }
        }
    }

    private static bool SupportsDomainEvent(object handler, Type domainEventType)
    {
        return handler.GetType().GetInterfaces().Any(x =>
            x.IsGenericType &&
            x.GetGenericTypeDefinition() == typeof(IDomainEventHandler<>) &&
            x.GenericTypeArguments[0] == domainEventType);
    }
}
