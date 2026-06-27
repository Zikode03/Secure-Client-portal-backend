using Microsoft.Extensions.DependencyInjection;
using SecureClientPortal.Backend.Application.Common.Events;

namespace SecureClientPortal.Backend.Infrastructure.Common.Events;

public sealed class IntegrationEventDispatcher : IIntegrationEventDispatcher
{
    private readonly IServiceProvider _serviceProvider;

    public IntegrationEventDispatcher(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task DispatchAsync(IEnumerable<IIntegrationEvent> integrationEvents, CancellationToken ct = default)
    {
        foreach (var integrationEvent in integrationEvents)
        {
            var handlerType = typeof(IIntegrationEventHandler<>).MakeGenericType(integrationEvent.GetType());
            var handlers = _serviceProvider.GetServices(handlerType);

            foreach (var handler in handlers)
            {
                await ((dynamic)handler).HandleAsync((dynamic)integrationEvent, ct);
            }
        }
    }
}
