using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application.Documents;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Models;
using System.Security.Claims;
using System.Text.Json;

namespace SecureClientPortal.Backend.Infrastructure.Documents;

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
        if (string.IsNullOrWhiteSpace(userId))
        {
            return (true, []);
        }

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        await _db.AddDeadlineApproachingNotificationsAsync(
            user.Identity?.IsAuthenticated == true ? user : new ClaimsPrincipal(new ClaimsIdentity()),
            userId,
            allowedClientIds,
            ct);

        var data = await _db.Notifications
            .Where(x => x.UserId == userId && (x.ClientId == null || allowedClientIds.Contains(x.ClientId)))
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);

        return (false, data);
    }

    public async Task<(bool unauthorized, bool forbidden, Notification? item)> MarkAsReadAsync(string id, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var userId = user.GetUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return (true, false, null);
        }

        var item = await _db.Notifications.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);
        if (item is null)
        {
            return (false, false, null);
        }

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (item.ClientId is not null && !allowedClientIds.Contains(item.ClientId))
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
        if (string.IsNullOrWhiteSpace(userId))
        {
            return (true, 0);
        }

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        var items = await _db.Notifications
            .Where(x => x.UserId == userId && !x.IsRead && (x.ClientId == null || allowedClientIds.Contains(x.ClientId)))
            .ToListAsync(ct);

        foreach (var item in items)
        {
            item.MarkRead();
        }

        await _db.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(user, "notification.read_all", "notification", userId, null, JsonSerializer.Serialize(new { count = items.Count }), ct);
        return (false, items.Count);
    }
}
