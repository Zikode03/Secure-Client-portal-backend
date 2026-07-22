using Microsoft.Extensions.DependencyInjection;
using SecureClientPortal.Backend.Application.Common.Events;
using System.Reflection;

namespace SecureClientPortal.Backend.Infrastructure.Common.Events;

public sealed class DomainEventDispatcher : IDomainEventDispatcher
{
    private static readonly MethodInfo DispatchDomainEventMethod =
        typeof(DomainEventDispatcher).GetMethod(nameof(DispatchDomainEventAsync), BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"Unable to locate {nameof(DispatchDomainEventAsync)}.");

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
            var dispatchTask = DispatchDomainEventMethod
                .MakeGenericMethod(domainEvent.GetType())
                .Invoke(this, [domainEvent, ct]) as Task
                ?? throw new InvalidOperationException($"Unable to dispatch domain event of type '{domainEvent.GetType().FullName}'.");

            await dispatchTask;
        }
    }

    private async Task DispatchDomainEventAsync<TDomainEvent>(TDomainEvent domainEvent, CancellationToken ct)
        where TDomainEvent : IDomainEvent
    {
        var handlers = _serviceProvider.GetServices<IDomainEventHandler<TDomainEvent>>();

        foreach (var handler in handlers)
        {
            var integrationEvents = await handler.HandleAsync(domainEvent, ct);
            await _integrationEventDispatcher.DispatchAsync(integrationEvents, ct);
        }
    }
}
