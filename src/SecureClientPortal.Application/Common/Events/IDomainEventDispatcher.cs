using SecureClientPortal.Backend.Models;

namespace SecureClientPortal.Backend.Application.Common.Events;

public interface IDomainEventDispatcher
{
    Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken ct = default);
}
