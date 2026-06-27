namespace SecureClientPortal.Backend.Models;

public interface IDomainEvent
{
    DateTime OccurredAtUtc { get; }
}
