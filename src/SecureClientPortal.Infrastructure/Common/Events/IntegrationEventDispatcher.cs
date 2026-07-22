using Microsoft.Extensions.DependencyInjection;
using SecureClientPortal.Backend.Application.Common.Events;
using System.Reflection;

namespace SecureClientPortal.Backend.Infrastructure.Common.Events;

public sealed class IntegrationEventDispatcher : IIntegrationEventDispatcher
{
    private static readonly MethodInfo DispatchIntegrationEventMethod =
        typeof(IntegrationEventDispatcher).GetMethod(nameof(DispatchIntegrationEventAsync), BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"Unable to locate {nameof(DispatchIntegrationEventAsync)}.");

    private readonly IServiceProvider _serviceProvider;

    public IntegrationEventDispatcher(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task DispatchAsync(IEnumerable<IIntegrationEvent> integrationEvents, CancellationToken ct = default)
    {
        foreach (var integrationEvent in integrationEvents)
        {
            var dispatchTask = DispatchIntegrationEventMethod
                .MakeGenericMethod(integrationEvent.GetType())
                .Invoke(this, [integrationEvent, ct]) as Task
                ?? throw new InvalidOperationException($"Unable to dispatch integration event of type '{integrationEvent.GetType().FullName}'.");

            await dispatchTask;
        }
    }

    private async Task DispatchIntegrationEventAsync<TIntegrationEvent>(TIntegrationEvent integrationEvent, CancellationToken ct)
        where TIntegrationEvent : IIntegrationEvent
    {
        var handlers = _serviceProvider.GetServices<IIntegrationEventHandler<TIntegrationEvent>>();

        foreach (var handler in handlers)
        {
            await handler.HandleAsync(integrationEvent, ct);
        }
    }
}
