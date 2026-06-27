using SecureClientPortal.Backend.Models;

namespace SecureClientPortal.Backend.Application.Notifications.Events;

public sealed record NotificationRequestedIntegrationEvent(
    Guid? ActorUserId,
    string ActorRole,
    Guid ClientId,
    string AudienceRole,
    string Type,
    string Title,
    string Message,
    string? LinkUrl,
    object? Metadata,
    DateTime OccurredAtUtc) : IIntegrationEvent;
