using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SecureClientPortal.Backend.Application.Modules.Notifications;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Models;

namespace SecureClientPortal.Backend.Infrastructure.Modules.Notifications;

public sealed class NotificationEmailDispatcher(PortalDbContext db, INotificationEmailSender sender, ILogger<NotificationEmailDispatcher> logger)
{
    public async Task<int> RunBatchAsync(DateTime utcNow, CancellationToken ct)
    {
        var jobs = await db.NotificationEmailDeliveries.Where(x =>
            (x.Status == "pending" || x.Status == "sending") && x.NextAttemptAtUtc <= utcNow)
            .OrderBy(x => x.NextAttemptAtUtc).Take(20).ToListAsync(ct);
        var sent = 0;
        foreach (var job in jobs)
        {
            // Optimistic claim prevents concurrent workers from sending the same job.
            job.Status = "sending"; job.NextAttemptAtUtc = utcNow.AddMinutes(15); job.Version = Guid.NewGuid();
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { db.Entry(job).State = EntityState.Detached; continue; }
            var notification = await db.Notifications.AsNoTracking().FirstOrDefaultAsync(x => x.Id == job.NotificationId, ct);
            var user = notification is null ? null : await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == notification.UserId, ct);
            var preference = user is null ? null : await db.NotificationPreferences.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == user.Id, ct);
            if (notification is null || user is null || notification.IsRead || notification.CreatedAtUtc < utcNow.AddDays(-7) ||
                preference?.EmailReminders == false || UserSecurityProfile.GetStatus(user.SecurityJson) != "active")
            { await Skip(job, "not_eligible", ct); continue; }
            var scope = await db.RoleDefinitions.Where(x => x.Name == user.Role).Select(x => x.Scope).FirstOrDefaultAsync(ct) ?? user.Role;
            var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] {new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim("role_scope", scope), new Claim(ClaimTypes.Role, user.Role)}, "notification-email"));
            if (notification.ClientId.HasValue && !(await principal.GetAccessibleClientIdsAsync(db, ct)).Contains(notification.ClientId.Value))
            { await Skip(job, "access_removed", ct); continue; }
            // Quiet hours delay emails rather than discarding them. In-app alerts remain available.
            var quiet = preference?.QuietHours ?? "22:00-06:00";
            var times = quiet.Split('-');
            if (times.Length == 2 && TimeOnly.TryParse(times[0], out var start) && TimeOnly.TryParse(times[1], out var end) && start != end)
            {
                var now = TimeOnly.FromDateTime(utcNow.AddHours(2));
                if (start < end ? now >= start && now < end : now >= start || now < end)
                { job.Status = "pending"; job.NextAttemptAtUtc = utcNow.AddMinutes(5); await db.SaveChangesAsync(ct); continue; }
            }
            // Re-evaluate category opt-outs at delivery, including changes after the job was queued.
            if (!NotificationDeliveryPolicy.Allows(preference, notification.Type, utcNow))
            { await Skip(job, "preference_disabled", ct); continue; }
            job.Attempts++;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                // Keep document contents out of email; the authenticated portal contains the details.
                var path = scope == "client" ? "/client/notifications" : "/firm/notifications";
                await sender.SendAsync(user.Email, user.FullName, notification.Title, path, notification.Id, timeout.Token);
                job.Status = "sent"; job.SentAtUtc = utcNow; job.FailureCode = null; sent++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                job.Status = job.Attempts >= 5 ? "failed" : "pending";
                job.FailureCode = "smtp_delivery_failed";
                job.NextAttemptAtUtc = utcNow.AddMinutes(Math.Pow(2, job.Attempts));
                logger.LogWarning("Notification email {NotificationId} attempt {Attempt} failed ({ExceptionType}).", job.NotificationId, job.Attempts, ex.GetType().Name);
            }
            job.Version = Guid.NewGuid();
            await db.SaveChangesAsync(ct);
        }
        return sent;
    }
    private async Task Skip(NotificationEmailDelivery job, string reason, CancellationToken ct)
    { job.Status = "skipped"; job.FailureCode = reason; job.Version = Guid.NewGuid(); await db.SaveChangesAsync(ct); }
}
