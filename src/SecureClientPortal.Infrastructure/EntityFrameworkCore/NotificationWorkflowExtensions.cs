using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Models;
using System.Security.Claims;
using System.Text.Json;

namespace SecureClientPortal.Backend.Data;

public static class NotificationWorkflowExtensions
{
    public static async Task<List<Guid>> ResolveNotificationRecipientsAsync(
        this PortalDbContext db,
        Guid clientId,
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

        if (audienceRole == "client")
        {
            var users = await db.Users
                .Where(x => roleNames.Contains(x.Role))
                .ToListAsync(ct);

            return users
                .Where(x => ClientHasAccess(x.ClientIdsJson, clientId))
                .Select(x => x.Id)
                .Distinct()
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
        ClaimsPrincipal actor,
        IEnumerable<Guid> recipientUserIds,
        Guid clientId,
        string type,
        string title,
        string message,
        string? linkUrl = null,
        object? metadata = null,
        CancellationToken ct = default)
    {
        var actorUserId = actor.GetUserId();
        var recipients = recipientUserIds
            .Where(x => x != Guid.Empty && x != actorUserId)
            .Distinct()
            .ToList();

        var batchId = Guid.NewGuid();

        foreach (var recipientUserId in recipients)
        {
            db.Notifications.Add(Notification.Create(
                Guid.NewGuid(),
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
                "notification_batch",
                batchId,
                clientId,
                JsonSerializer.Serialize(new { type, title, recipientUserIds = recipients, metadata }),
                ct);
        }

        return recipients.Count;
    }

    public static async Task<int> AddDeadlineApproachingNotificationsAsync(
        this PortalDbContext db,
        ClaimsPrincipal actor,
        Guid userId,
        IReadOnlyCollection<Guid> allowedClientIds,
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
        var batchId = Guid.NewGuid();

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
                Guid.NewGuid(),
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
                "notification_batch",
                batchId,
                null,
                JsonSerializer.Serialize(new { type = "deadline.approaching", userId, count = createdCount }),
                ct);
        }

        return createdCount;
    }

    private static bool ClientHasAccess(string clientIdsJson, Guid clientId)
    {
        if (string.IsNullOrWhiteSpace(clientIdsJson))
        {
            return false;
        }

        try
        {
            var clientIds = JsonSerializer.Deserialize<string[]>(clientIdsJson) ?? [];
            return clientIds.Any(x => Guid.TryParse(x, out var parsedId) && parsedId == clientId);
        }
        catch
        {
            return false;
        }
    }
}
