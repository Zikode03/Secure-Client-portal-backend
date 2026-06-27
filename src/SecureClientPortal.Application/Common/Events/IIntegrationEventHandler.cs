using SecureClientPortal.Backend.Models;

namespace SecureClientPortal.Backend.Application.Common.Events;

public interface IIntegrationEventHandler<in TIntegrationEvent> where TIntegrationEvent : IIntegrationEvent
{
    Task HandleAsync(TIntegrationEvent integrationEvent, CancellationToken ct = default);
}
