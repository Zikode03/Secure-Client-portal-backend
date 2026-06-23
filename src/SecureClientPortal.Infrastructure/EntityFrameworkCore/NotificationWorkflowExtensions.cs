using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Models;
using System.Security.Claims;
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
        var roleNames = await db.RoleDefinitions
            .Where(x => x.Scope == audienceRole)
            .Select(x => x.Name)
            .ToListAsync(ct);

        if (roleNames.Count == 0)
        {
            roleNames = [audienceRole];
        }

        // Centralize role-scope recipient lookup so workflow handlers do not depend on hard-coded role names.
        if (audienceRole == "client")
        {
            var users = await db.Users
                .Where(x => roleNames.Contains(x.Role))
                .ToListAsync(ct);

            return users
                .Where(x => ClientHasAccess(x.ClientIdsJson, clientId))
                .Select(x => x.Id)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (audienceRole == "accountant")
        {
            var accountantIds = await db.Users
                .Where(x => roleNames.Contains(x.Role))
                .Select(x => x.Id)
                .ToListAsync(ct);

            return await db.ClientAssignments
                .Where(x => x.ClientId == clientId)
                .Where(x => accountantIds.Contains(x.AccountantUserId))
                .Select(x => x.AccountantUserId)
                .Distinct()
                .ToListAsync(ct);
        }

        if (audienceRole == "admin")
        {
            return await db.Users
                .Where(x => roleNames.Contains(x.Role))
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
            db.Notifications.Add(Notification.Create(
                $"ntf_{Guid.NewGuid():N}",
                recipientUserId,
                clientId,
                type,
                title,
                message,
                linkUrl));
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

    public static async Task<int> AddDeadlineApproachingNotificationsAsync(
        this PortalDbContext db,
        ClaimsPrincipal actor,
        string userId,
        IReadOnlyCollection<string> allowedClientIds,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var deadlineWindowEnd = now.AddDays(3);

        var requests = await db.Requests
            .Where(x =>
                allowedClientIds.Contains(x.ClientId) &&
                x.DueDateUtc != null &&
                x.DueDateUtc >= now &&
                x.DueDateUtc <= deadlineWindowEnd &&
                x.Status != "resolved")
            .ToListAsync(ct);

        var createdCount = 0;

        foreach (var request in requests)
        {
            var linkUrl = $"/requests/{request.Id}";
            var alreadyExists = await db.Notifications.AnyAsync(x =>
                x.UserId == userId &&
                x.Type == "deadline.approaching" &&
                x.LinkUrl == linkUrl &&
                x.CreatedAtUtc >= now.AddHours(-24), ct);

            if (alreadyExists)
            {
                continue;
            }

            db.Notifications.Add(Notification.Create(
                $"ntf_{Guid.NewGuid():N}",
                userId,
                request.ClientId,
                "deadline.approaching",
                "Deadline approaching",
                $"Request '{request.Title}' is due on {request.DueDateUtc:yyyy-MM-dd HH:mm} UTC.",
                linkUrl,
                now));

            createdCount++;
        }

        if (createdCount > 0)
        {
            await db.SaveChangesAsync(ct);
            await db.WriteAuditLogAsync(
                actor,
                "notification.sent",
                "notification",
                $"deadline.approaching:{userId}:{Guid.NewGuid():N}",
                null,
                JsonSerializer.Serialize(new { type = "deadline.approaching", userId, count = createdCount }),
                ct);
        }

        return createdCount;
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
