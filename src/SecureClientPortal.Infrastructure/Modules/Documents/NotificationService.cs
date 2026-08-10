using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts.Modules.Notifications;
using SecureClientPortal.Backend.Application.Modules.Notifications;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Models;
using System.Security.Claims;
using System.Text.Json;

namespace SecureClientPortal.Backend.Infrastructure.Modules.Notifications;

public sealed class NotificationService : INotificationService
{
    private readonly PortalDbContext _db;

    public NotificationService(PortalDbContext db)
    {
        _db = db;
    }

    public async Task<(bool unauthorized, IReadOnlyList<Notification> items)> GetMineAsync(ClaimsPrincipal user, CancellationToken ct = default)
    {
        var userId = user.GetUserId();
        if (!userId.HasValue || userId == Guid.Empty)
        {
            return (true, []);
        }

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        await _db.AddDeadlineApproachingNotificationsAsync(
            user.Identity?.IsAuthenticated == true ? user : new ClaimsPrincipal(new ClaimsIdentity()),
            userId.Value,
            allowedClientIds,
            ct);

        var data = await _db.Notifications
            .Where(x => x.UserId == userId.Value && (x.ClientId == null || allowedClientIds.Contains(x.ClientId.Value)))
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);

        return (false, data);
    }

    public async Task<(bool unauthorized, bool forbidden, Notification? item)> MarkAsReadAsync(string id, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var userId = user.GetUserId();
        if (!userId.HasValue || userId == Guid.Empty)
        {
            return (true, false, null);
        }

        if (!Guid.TryParse(id, out var notificationId))
        {
            return (false, false, null);
        }

        var item = await _db.Notifications.FirstOrDefaultAsync(x => x.Id == notificationId && x.UserId == userId.Value, ct);
        if (item is null)
        {
            return (false, false, null);
        }

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (item.ClientId is not null && !allowedClientIds.Contains(item.ClientId.Value))
        {
            return (false, true, null);
        }

        item.MarkRead();
        await _db.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(user, "notification.read", "notification", item.Id, item.ClientId, JsonSerializer.Serialize(new { item.Type }), ct);
        return (false, false, item);
    }

    public async Task<(bool unauthorized, int updated)> MarkAllReadAsync(ClaimsPrincipal user, CancellationToken ct = default)
    {
        var userId = user.GetUserId();
        if (!userId.HasValue || userId == Guid.Empty)
        {
            return (true, 0);
        }

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        var items = await _db.Notifications
            .Where(x => x.UserId == userId.Value && !x.IsRead && (x.ClientId == null || allowedClientIds.Contains(x.ClientId.Value)))
            .ToListAsync(ct);

        foreach (var item in items)
        {
            item.MarkRead();
        }

        await _db.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(user, "notification.read_all", "notification_batch", userId.Value, null, JsonSerializer.Serialize(new { count = items.Count }), ct);
        return (false, items.Count);
    }

    public async Task<ServiceResult<NotificationPreferenceResponse>> GetPreferencesAsync(ClaimsPrincipal user, CancellationToken ct = default)
    {
        var userId = user.GetUserId();
        if (!userId.HasValue || userId.Value == Guid.Empty)
        {
            return ServiceResult<NotificationPreferenceResponse>.UnauthorizedResult();
        }

        var preference = await _db.NotificationPreferences.FirstOrDefaultAsync(x => x.UserId == userId.Value, ct);
        return ServiceResult<NotificationPreferenceResponse>.Success(
            preference is null ? DefaultPreferences() : MapPreferences(preference));
    }

    public async Task<ServiceResult<NotificationPreferenceResponse>> UpdatePreferencesAsync(
        UpdateNotificationPreferenceRequest request,
        ClaimsPrincipal user,
        CancellationToken ct = default)
    {
        var userId = user.GetUserId();
        if (!userId.HasValue || userId.Value == Guid.Empty)
        {
            return ServiceResult<NotificationPreferenceResponse>.UnauthorizedResult();
        }

        var preference = await _db.NotificationPreferences.FirstOrDefaultAsync(x => x.UserId == userId.Value, ct);
        var isNew = preference is null;
        if (preference is null)
        {
            preference = NotificationPreference.Create(userId.Value);
        }

        try
        {
            preference.Update(
                request.DeadlineAlerts,
                request.RejectionAlerts,
                request.ComplianceAlerts,
                request.WeeklySummary,
                request.BrowserAlerts,
                request.EmailReminders,
                request.EscalationAlerts,
                request.QuietHours);
        }
        catch (DomainRuleException ex)
        {
            return ServiceResult<NotificationPreferenceResponse>.ErrorResult(ex.Message);
        }

        if (isNew)
        {
            _db.NotificationPreferences.Add(preference);
        }

        await _db.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(
            user,
            "notification.preferences_updated",
            "notification_preference",
            userId.Value,
            null,
            JsonSerializer.Serialize(new
            {
                preference.DeadlineAlerts,
                preference.RejectionAlerts,
                preference.ComplianceAlerts,
                preference.WeeklySummary,
                preference.BrowserAlerts,
                preference.EmailReminders,
                preference.EscalationAlerts,
                preference.QuietHours
            }),
            ct);

        return ServiceResult<NotificationPreferenceResponse>.Success(MapPreferences(preference));
    }

    private static NotificationPreferenceResponse DefaultPreferences() =>
        new(true, true, true, false, false, true, true, "22:00-06:00", null);

    private static NotificationPreferenceResponse MapPreferences(NotificationPreference preference) =>
        new(
            preference.DeadlineAlerts,
            preference.RejectionAlerts,
            preference.ComplianceAlerts,
            preference.WeeklySummary,
            preference.BrowserAlerts,
            preference.EmailReminders,
            preference.EscalationAlerts,
            preference.QuietHours,
            preference.UpdatedAtUtc);
}
