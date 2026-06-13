using Microsoft.AspNetCore.Http;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Models;

namespace SecureClientPortal.Backend.Data;

public static class AuditLoggingExtensions
{
    public static async Task WriteAuditLogAsync(
        this PortalDbContext db,
        string? actorUserId,
        string actorRole,
        string action,
        string entityType,
        string entityId,
        string? clientId = null,
        string? metadataJson = null,
        CancellationToken ct = default)
    {
        db.AuditLogs.Add(new AuditLog
        {
            Id = $"al_{Guid.NewGuid():N}",
            ActorUserId = actorUserId,
            ActorRole = actorRole,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            ClientId = clientId,
            MetadataJson = metadataJson,
            CreatedAtUtc = DateTime.UtcNow
        });

        await db.SaveChangesAsync(ct);
    }

    public static async Task WriteAuditLogAsync(
        this PortalDbContext db,
        System.Security.Claims.ClaimsPrincipal user,
        string action,
        string entityType,
        string entityId,
        string? clientId = null,
        string? metadataJson = null,
        CancellationToken ct = default)
    {
        var actorRole = user.IsAdmin() ? "admin" : user.IsAccountant() ? "accountant" : user.IsClient() ? "client" : "unknown";
        await db.WriteAuditLogAsync(user.GetUserId(), actorRole, action, entityType, entityId, clientId, metadataJson, ct);
    }

    public static async Task WriteDocumentAccessLogAsync(
        this PortalDbContext db,
        System.Security.Claims.ClaimsPrincipal user,
        HttpContext httpContext,
        Document document,
        string action,
        string? metadataJson = null,
        CancellationToken ct = default)
    {
        var actorRole = user.IsAdmin() ? "admin" : user.IsAccountant() ? "accountant" : user.IsClient() ? "client" : "unknown";
        db.DocumentAccessLogs.Add(new DocumentAccessLog
        {
            Id = $"dal_{Guid.NewGuid():N}",
            DocumentId = document.Id,
            ClientId = document.ClientId,
            AccessedByUserId = user.GetUserId(),
            AccessedByRole = actorRole,
            Action = action,
            IpAddress = httpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = httpContext.Request.Headers.UserAgent.ToString(),
            MetadataJson = metadataJson,
            AccessedAtUtc = DateTime.UtcNow
        });

        await db.SaveChangesAsync(ct);
    }
}
