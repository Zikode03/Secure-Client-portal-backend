namespace SecureClientPortal.Backend.Application.Identity;

public record AccessEmailDispatchResult(string DeliveryMode, string? PreviewUrl = null);

public interface IAccessEmailSender
{
    Task<AccessEmailDispatchResult> SendInviteAsync(string recipientEmail, string recipientName, string setupUrl, DateTime expiresAtUtc, CancellationToken ct);
    Task<AccessEmailDispatchResult> SendPasswordResetAsync(string recipientEmail, string recipientName, string setupUrl, DateTime expiresAtUtc, CancellationToken ct);
}

public interface IAccessLinkBuilder
{
    string BuildSetupUrl(string email, string token);
    string BuildPasswordResetUrl(string email, string token);
}
