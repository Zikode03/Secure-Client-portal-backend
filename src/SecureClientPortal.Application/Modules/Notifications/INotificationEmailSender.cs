namespace SecureClientPortal.Backend.Application.Modules.Notifications;

public interface INotificationEmailSender
{
    Task SendAsync(string email, string recipientName, string title, string portalPath, Guid notificationId, CancellationToken ct);
}
