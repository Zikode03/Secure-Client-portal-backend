using SecureClientPortal.Backend.Application.Common.Events;

namespace SecureClientPortal.Backend.Infrastructure.Common.Events;

public sealed class StandaloneIntegrationEventDispatcher : IIntegrationEventDispatcher
{
    private readonly IReadOnlyCollection<object> _handlers;

    public StandaloneIntegrationEventDispatcher(IEnumerable<object> handlers)
    {
        _handlers = handlers.ToArray();
    }

    public async Task DispatchAsync(IEnumerable<IIntegrationEvent> integrationEvents, CancellationToken ct = default)
    {
        foreach (var integrationEvent in integrationEvents)
        {
            foreach (var handler in _handlers.Where(x => SupportsIntegrationEvent(x, integrationEvent.GetType())))
            {
                await ((dynamic)handler).HandleAsync((dynamic)integrationEvent, ct);
            }
        }
    }

    private static bool SupportsIntegrationEvent(object handler, Type integrationEventType)
    {
        return handler.GetType().GetInterfaces().Any(x =>
            x.IsGenericType &&
            x.GetGenericTypeDefinition() == typeof(IIntegrationEventHandler<>) &&
            x.GenericTypeArguments[0] == integrationEventType);
    }
}
