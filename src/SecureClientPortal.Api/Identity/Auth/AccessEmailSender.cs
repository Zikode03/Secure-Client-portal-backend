using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace SecureClientPortal.Backend.Auth;

public record AccessEmailDispatchResult(string DeliveryMode, string? PreviewUrl = null);

public interface IAccessEmailSender
{
    Task<AccessEmailDispatchResult> SendInviteAsync(string recipientEmail, string recipientName, string setupUrl, DateTime expiresAtUtc, CancellationToken ct);
    Task<AccessEmailDispatchResult> SendPasswordResetAsync(string recipientEmail, string recipientName, string setupUrl, DateTime expiresAtUtc, CancellationToken ct);
}

public class AccessEmailSender : IAccessEmailSender
{
    private readonly AccessEmailOptions _options;
    private readonly ILogger<AccessEmailSender> _logger;

    public AccessEmailSender(IOptions<AccessEmailOptions> options, ILogger<AccessEmailSender> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task<AccessEmailDispatchResult> SendInviteAsync(
        string recipientEmail,
        string recipientName,
        string setupUrl,
        DateTime expiresAtUtc,
        CancellationToken ct)
    {
        return SendAsync(
            recipientEmail,
            recipientName,
            "You're invited to Secure Client Portal",
            $"""
            Hello {recipientName},

            An administrator created your Secure Client Portal access.

            Complete your account setup here:
            {setupUrl}

            This link expires on {expiresAtUtc:O}.
            """,
            setupUrl,
            ct);
    }

    public Task<AccessEmailDispatchResult> SendPasswordResetAsync(
        string recipientEmail,
        string recipientName,
        string setupUrl,
        DateTime expiresAtUtc,
        CancellationToken ct)
    {
        return SendAsync(
            recipientEmail,
            recipientName,
            "Reset your Secure Client Portal password",
            $"""
            Hello {recipientName},

            A password reset was requested for your Secure Client Portal account.

            Complete your password reset here:
            {setupUrl}

            This link expires on {expiresAtUtc:O}.
            """,
            setupUrl,
            ct);
    }

    private async Task<AccessEmailDispatchResult> SendAsync(
        string recipientEmail,
        string recipientName,
        string subject,
        string body,
        string previewUrl,
        CancellationToken ct)
    {
        if (!_options.Enabled || !string.Equals(_options.DeliveryMode, "smtp", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation(
                "Access email not sent via SMTP. Mode={Mode}; To={Email}; Subject={Subject}; PreviewUrl={PreviewUrl}",
                _options.DeliveryMode,
                recipientEmail,
                subject,
                previewUrl);
            return new AccessEmailDispatchResult(_options.DeliveryMode, previewUrl);
        }

        if (string.IsNullOrWhiteSpace(_options.SmtpHost))
        {
            _logger.LogWarning("AccessEmail is enabled for SMTP but SmtpHost is missing. Falling back to log mode.");
            return new AccessEmailDispatchResult("log", previewUrl);
        }

        using var message = new MailMessage
        {
            From = new MailAddress(_options.FromEmail, _options.FromName),
            Subject = subject,
            Body = body,
            IsBodyHtml = false
        };
        message.To.Add(new MailAddress(recipientEmail, recipientName));

        using var client = new SmtpClient(_options.SmtpHost, _options.SmtpPort)
        {
            EnableSsl = _options.UseSsl
        };
        if (!string.IsNullOrWhiteSpace(_options.SmtpUsername))
        {
            client.Credentials = new NetworkCredential(_options.SmtpUsername, _options.SmtpPassword);
        }

        using var registration = ct.Register(client.SendAsyncCancel);
        await client.SendMailAsync(message);
        return new AccessEmailDispatchResult("smtp", previewUrl);
    }
}
