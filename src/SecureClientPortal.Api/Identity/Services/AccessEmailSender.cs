using System.Net;
using System.Net.Mail;
using System.Text;
using Microsoft.Extensions.Options;
using SecureClientPortal.Backend.Application.Identity;

namespace SecureClientPortal.Backend.Auth;

public class AccessEmailSender : IAccessEmailSender
{
    private readonly AccessEmailOptions _options;
    private readonly ILogger<AccessEmailSender> _logger;

    private const string BrandName = "Secure Client Portal";
    private const string BrandAccent = "#18ac5f";
    private const string BrandTeal = "#074e5f";
    private const string BrandNavy = "#0a2f66";
    private const string Surface = "#eef4fa";
    private const string Border = "#d7e3ee";
    private const string TextPrimary = "#091333";
    private const string TextSecondary = "#53617f";

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
            $"Complete your {BrandName} setup",
            BuildInvitePlainText(recipientName, setupUrl, expiresAtUtc),
            BuildInviteHtml(recipientName, setupUrl, expiresAtUtc),
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
            $"Reset your {BrandName} password",
            BuildPasswordResetPlainText(recipientName, setupUrl, expiresAtUtc),
            BuildPasswordResetHtml(recipientName, setupUrl, expiresAtUtc),
            setupUrl,
            ct);
    }

    private async Task<AccessEmailDispatchResult> SendAsync(
        string recipientEmail,
        string recipientName,
        string subject,
        string textBody,
        string htmlBody,
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
            Body = textBody,
            BodyEncoding = Encoding.UTF8,
            SubjectEncoding = Encoding.UTF8,
            IsBodyHtml = false
        };
        message.To.Add(new MailAddress(recipientEmail, recipientName));
        message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(textBody, Encoding.UTF8, "text/plain"));
        message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(htmlBody, Encoding.UTF8, "text/html"));

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

    private static string BuildInvitePlainText(string recipientName, string setupUrl, DateTime expiresAtUtc) =>
        $"""
        Hello {recipientName},

        An administrator created your Secure Client Portal access.

        Complete your account setup here:
        {setupUrl}

        This link expires on {expiresAtUtc:dddd, dd MMMM yyyy 'at' HH:mm 'UTC'}.

        If you were not expecting this email, you can safely ignore it.
        """;

    private static string BuildPasswordResetPlainText(string recipientName, string setupUrl, DateTime expiresAtUtc) =>
        $"""
        Hello {recipientName},

        We received a request to reset your Secure Client Portal password.

        Reset your password here:
        {setupUrl}

        This link expires on {expiresAtUtc:dddd, dd MMMM yyyy 'at' HH:mm 'UTC'}.

        If you did not request a reset, you can safely ignore this email.
        """;

    private static string BuildInviteHtml(string recipientName, string setupUrl, DateTime expiresAtUtc) =>
        BuildEmailHtml(
            preheader: "Complete your account setup and choose your password.",
            title: "You’ve been invited to join Secure Client Portal.",
            greeting: $"Hello {WebUtility.HtmlEncode(recipientName)},",
            body:
                $"An administrator created your <strong>{BrandName}</strong> access. Get started below by creating your password and finishing your account setup.",
            actionLabel: "Join This Account",
            actionUrl: setupUrl,
            footerNote: "If you were not expecting this email, you can safely ignore it.",
            expiresAtUtc: expiresAtUtc);

    private static string BuildPasswordResetHtml(string recipientName, string setupUrl, DateTime expiresAtUtc) =>
        BuildEmailHtml(
            preheader: "Reset your password and regain access to your account.",
            title: "Reset your Secure Client Portal password.",
            greeting: $"Hello {WebUtility.HtmlEncode(recipientName)},",
            body:
                $"We received a request to reset your <strong>{BrandName}</strong> password. Use the button below to choose a new password and regain access.",
            actionLabel: "Reset password",
            actionUrl: setupUrl,
            footerNote: "If you did not request a password reset, you can safely ignore this email.",
            expiresAtUtc: expiresAtUtc);

    private static string BuildEmailHtml(
        string preheader,
        string title,
        string greeting,
        string body,
        string actionLabel,
        string actionUrl,
        string footerNote,
        DateTime expiresAtUtc)
    {
        var encodedActionUrl = WebUtility.HtmlEncode(actionUrl);
        var encodedPreheader = WebUtility.HtmlEncode(preheader);
        var encodedTitle = WebUtility.HtmlEncode(title);
        var encodedFooter = WebUtility.HtmlEncode(footerNote);
        var expiry = WebUtility.HtmlEncode(expiresAtUtc.ToString("dddd, dd MMMM yyyy 'at' HH:mm 'UTC'"));

        return $"""
        <!doctype html>
        <html lang="en">
          <head>
            <meta charset="utf-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1.0" />
            <title>{encodedTitle}</title>
          </head>
          <body style="margin:0;padding:0;background-color:{Surface};font-family:Segoe UI,Arial,sans-serif;color:{TextPrimary};">
            <div style="display:none;max-height:0;overflow:hidden;opacity:0;">
              {encodedPreheader}
            </div>
            <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background-color:{Surface};padding:32px 16px;">
              <tr>
                <td align="center">
                  <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:680px;">
                    <tr>
                      <td align="center" style="padding:40px 20px 96px;background:linear-gradient(135deg,{BrandTeal} 0%, #49b5cf 100%);border-radius:24px 24px 0 0;">
                        <div style="display:inline-block;border-radius:20px;background:rgba(255,255,255,0.16);padding:12px 16px;box-shadow:inset 0 0 0 1px rgba(255,255,255,0.18);">
                          <div style="font-size:13px;font-weight:800;letter-spacing:0.18em;text-transform:uppercase;color:#ffffff;">SCP</div>
                        </div>
                      </td>
                    </tr>
                    <tr>
                      <td style="padding:0 20px 24px;">
                        <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="margin-top:-64px;background:#ffffff;border:1px solid {Border};border-radius:6px;box-shadow:0 22px 44px rgba(4,24,52,0.10);">
                          <tr>
                            <td style="padding:40px 40px 24px;">
                              <h1 style="margin:0 0 22px;font-size:28px;line-height:1.28;font-weight:800;color:{TextPrimary};letter-spacing:-0.02em;">{encodedTitle}</h1>
                              <p style="margin:0 0 16px;font-size:16px;line-height:1.75;color:{TextSecondary};">{greeting}</p>
                              <p style="margin:0 0 16px;font-size:16px;line-height:1.8;color:{TextSecondary};">{body}</p>
                              <p style="margin:0 0 28px;font-size:16px;line-height:1.8;color:{TextPrimary};font-weight:700;">This link will expire after 7 days.</p>
                              <table role="presentation" cellspacing="0" cellpadding="0" style="margin:0 auto 30px;">
                                <tr>
                                  <td align="center" bgcolor="{BrandAccent}" style="border-radius:4px;box-shadow:0 12px 24px rgba(24,172,95,0.22);">
                                    <a href="{encodedActionUrl}" style="display:inline-block;padding:16px 34px;font-size:17px;font-weight:800;color:#ffffff;text-decoration:none;">{WebUtility.HtmlEncode(actionLabel)}</a>
                                  </td>
                                </tr>
                              </table>
                              <p style="margin:0 0 12px;font-size:14px;line-height:1.7;color:{TextSecondary};">
                                If the button does not work, copy and paste this link into your browser:
                              </p>
                              <p style="margin:0 0 22px;padding:14px 16px;border:1px solid {Border};border-radius:12px;background:{Surface};word-break:break-word;font-size:13px;line-height:1.7;color:{TextPrimary};">
                                <a href="{encodedActionUrl}" style="color:{BrandNavy};text-decoration:none;">{encodedActionUrl}</a>
                              </p>
                              <p style="margin:0 0 8px;font-size:14px;line-height:1.7;color:{TextSecondary};">
                                This link expires on <strong style="color:{TextPrimary};">{expiry}</strong>.
                              </p>
                              <p style="margin:0;font-size:14px;line-height:1.7;color:{TextSecondary};">{encodedFooter}</p>
                            </td>
                          </tr>
                          <tr>
                            <td style="padding:0 40px 28px;">
                              <div style="height:1px;background:{Border};"></div>
                            </td>
                          </tr>
                          <tr>
                            <td align="center" style="padding:0 40px 34px;">
                              <p style="margin:0 0 6px;font-size:12px;line-height:1.7;color:#8a97ad;">© {DateTime.UtcNow:yyyy} {BrandName}. All rights reserved.</p>
                              <p style="margin:0;font-size:12px;line-height:1.7;color:#8a97ad;">Professional document control and compliance workflows for your firm.</p>
                            </td>
                          </tr>
                        </table>
                      </td>
                    </tr>
                    <tr>
                      <td style="font-size:0;line-height:0;">&nbsp;</td>
                    </tr>
                  </table>
                </td>
              </tr>
            </table>
          </body>
        </html>
        """;
    }
}
