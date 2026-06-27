using Microsoft.Extensions.DependencyInjection;
using SecureClientPortal.Backend.Application.Common.Events;

namespace SecureClientPortal.Backend.Infrastructure.Common.Events;

public sealed class DomainEventDispatcher : IDomainEventDispatcher
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IIntegrationEventDispatcher _integrationEventDispatcher;

    public DomainEventDispatcher(IServiceProvider serviceProvider, IIntegrationEventDispatcher integrationEventDispatcher)
    {
        _serviceProvider = serviceProvider;
        _integrationEventDispatcher = integrationEventDispatcher;
    }

    public async Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken ct = default)
    {
        foreach (var domainEvent in domainEvents)
        {
            var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(domainEvent.GetType());
            var handlers = _serviceProvider.GetServices(handlerType);

            foreach (var handler in handlers)
            {
                var integrationEvents = await ((dynamic)handler).HandleAsync((dynamic)domainEvent, ct);
                await _integrationEventDispatcher.DispatchAsync(integrationEvents, ct);
            }
        }
    }
}
