namespace SecureClientPortal.Backend.Models;

public interface IHasDomainEvents
{
    IReadOnlyCollection<IDomainEvent> DequeueDomainEvents();
}
