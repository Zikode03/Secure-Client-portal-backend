using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SecureClientPortal.Backend.Application.Modules.Notifications;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Infrastructure.Modules.Notifications;

namespace SecureClientPortal.Backend.Auth;

public sealed class NotificationEmailOptions { public bool Enabled { get; set; } }

public sealed class SmtpNotificationEmailSender(AccessEmailSender sender, IOptions<PortalLinksOptions> links) : INotificationEmailSender
{
    public async Task SendAsync(string email, string name, string title, string path, Guid id, CancellationToken ct)
    {
        if (!Uri.TryCreate(links.Value.FrontendBaseUrl, UriKind.Absolute, out var origin) ||
            (origin.Scheme != "https" && !(origin.IsLoopback && origin.Scheme == "http")))
            throw new InvalidOperationException("Configure a secure PortalLinks:FrontendBaseUrl before notification delivery.");
        await sender.SendNotificationAsync(email, name, title, new Uri(origin, path).AbsoluteUri, id, ct);
    }
}

public sealed class NotificationEmailBackgroundService(IServiceScopeFactory scopes, IOptions<NotificationEmailOptions> options,
    IOptions<AccessEmailOptions> smtp, ILogger<NotificationEmailBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled || !smtp.Value.Enabled || !string.Equals(smtp.Value.DeliveryMode, "smtp", StringComparison.OrdinalIgnoreCase))
        { logger.LogInformation("Workflow notification email delivery is disabled; in-app notifications remain enabled."); return; }
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
                // Require an administrator's successful SMTP receipt verification for this exact configuration.
                var adminRoles = await db.RoleDefinitions.Where(x => x.Scope == "admin").Select(x => x.Name).ToListAsync(stoppingToken);
                adminRoles.Add("admin");
                var admins = await db.Users.AsNoTracking().Where(x => adminRoles.Contains(x.Role)).ToListAsync(stoppingToken);
                var verified = false;
                foreach (var admin in admins)
                {
                    if (UserSecurityProfile.GetStatus(admin.SecurityJson) != "active") continue;
                    var fingerprint = AccessTokenCodec.HashToken(System.Text.Json.JsonSerializer.Serialize(new { recipient = admin.Email, configuration = smtp.Value }));
                    verified = await db.AccountSecurities.AnyAsync(x => x.Id == admin.Id && x.SmtpVerifiedUtc != null && x.SmtpProbeHash == "verified:" + fingerprint, stoppingToken);
                    if (verified) break;
                }
                if (verified) await scope.ServiceProvider.GetRequiredService<NotificationEmailDispatcher>().RunBatchAsync(DateTime.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError("Notification email worker failed ({ExceptionType}); queued jobs will be retried.", ex.GetType().Name); }
            if (!await timer.WaitForNextTickAsync(stoppingToken)) break;
        }
    }
}
