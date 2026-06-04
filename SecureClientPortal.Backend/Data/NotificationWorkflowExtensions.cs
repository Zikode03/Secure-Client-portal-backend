using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Models;
using System.Text.Json;

namespace SecureClientPortal.Backend.Data;

public static class NotificationWorkflowExtensions
{
    public static async Task<List<string>> ResolveNotificationRecipientsAsync(
        this PortalDbContext db,
        string clientId,
        string audienceRole,
        CancellationToken ct = default)
    {
        // Centralize role-based recipient lookup so request and document handlers share one audience rule set.
        if (audienceRole == "client")
        {
            var users = await db.Users
                .Where(x => x.Role == "client")
                .ToListAsync(ct);

            return users
                .Where(x => ClientHasAccess(x.ClientIdsJson, clientId))
                .Select(x => x.Id)
                .ToList();
        }

        if (audienceRole == "accountant")
        {
            return await db.ClientAssignments
                .Where(x => x.ClientId == clientId)
                .Select(x => x.AccountantUserId)
                .Distinct()
                .ToListAsync(ct);
        }

        if (audienceRole == "admin")
        {
            return await db.Users
                .Where(x => x.Role == "admin")
                .Select(x => x.Id)
                .ToListAsync(ct);
        }

        return [];
    }

    public static async Task<int> AddNotificationsAsync(
        this PortalDbContext db,
        System.Security.Claims.ClaimsPrincipal actor,
        IEnumerable<string> recipientUserIds,
        string clientId,
        string type,
        string title,
        string message,
        string? linkUrl = null,
        object? metadata = null,
        CancellationToken ct = default)
    {
        var actorUserId = actor.GetUserId();
        var recipients = recipientUserIds
            .Where(x => !string.IsNullOrWhiteSpace(x) && !string.Equals(x, actorUserId, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var recipientUserId in recipients)
        {
            db.Notifications.Add(new Notification
            {
                Id = $"ntf_{Guid.NewGuid():N}",
                UserId = recipientUserId,
                ClientId = clientId,
                Type = type,
                Title = title,
                Message = message,
                LinkUrl = linkUrl,
                IsRead = false,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        if (recipients.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            await db.WriteAuditLogAsync(
                actor,
                "notification.sent",
                "notification",
                $"{type}:{clientId}:{Guid.NewGuid():N}",
                clientId,
                JsonSerializer.Serialize(new { type, title, recipientUserIds = recipients, metadata }),
                ct);
        }

        return recipients.Count;
    }

    private static bool ClientHasAccess(string clientIdsJson, string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientIdsJson))
        {
            return false;
        }

        try
        {
            var clientIds = JsonSerializer.Deserialize<string[]>(clientIdsJson) ?? [];
            return clientIds.Contains(clientId, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
