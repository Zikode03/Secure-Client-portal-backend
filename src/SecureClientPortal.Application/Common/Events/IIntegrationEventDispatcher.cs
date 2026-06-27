using SecureClientPortal.Backend.Models;

namespace SecureClientPortal.Backend.Application.Common.Events;

public interface IIntegrationEventDispatcher
{
    Task DispatchAsync(IEnumerable<IIntegrationEvent> integrationEvents, CancellationToken ct = default);
}
