using SecureClientPortal.Backend.Application.Common.Events;
using SecureClientPortal.Backend.Application.Notifications.Events;
using SecureClientPortal.Backend.Data;

namespace SecureClientPortal.Backend.Infrastructure.Notifications.Application;

public sealed class NotificationRequestedIntegrationEventHandler : IIntegrationEventHandler<NotificationRequestedIntegrationEvent>
{
    private readonly PortalDbContext _db;

    public NotificationRequestedIntegrationEventHandler(PortalDbContext db)
    {
        _db = db;
    }

    public async Task HandleAsync(NotificationRequestedIntegrationEvent integrationEvent, CancellationToken ct = default)
    {
        var recipients = await _db.ResolveNotificationRecipientsAsync(
            integrationEvent.ClientId,
            integrationEvent.AudienceRole,
            ct);

        await _db.AddNotificationsAsync(
            integrationEvent.ActorUserId,
            integrationEvent.ActorRole,
            recipients,
            integrationEvent.ClientId,
            integrationEvent.Type,
            integrationEvent.Title,
            integrationEvent.Message,
            integrationEvent.LinkUrl,
            integrationEvent.Metadata,
            ct);
    }
}
