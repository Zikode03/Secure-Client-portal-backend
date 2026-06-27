namespace SecureClientPortal.Backend.Models;

public interface IIntegrationEvent
{
    DateTime OccurredAtUtc { get; }
}
