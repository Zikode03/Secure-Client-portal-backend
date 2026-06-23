using Microsoft.AspNetCore.Http;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Models;

namespace SecureClientPortal.Backend.Data;

public static class AuditLoggingExtensions
{
    public static async Task WriteAuditLogAsync(
        this PortalDbContext db,
        Guid? actorUserId,
        string actorRole,
        string action,
        string entityType,
        Guid entityId,
        Guid? clientId = null,
        string? metadataJson = null,
        CancellationToken ct = default)
    {
        db.AuditLogs.Add(AuditLog.Create(
            Guid.NewGuid(),
            actorUserId,
            actorRole,
            action,
            entityType,
            entityId,
            clientId,
            metadataJson));

        await db.SaveChangesAsync(ct);
    }

    public static async Task WriteAuditLogAsync(
        this PortalDbContext db,
        System.Security.Claims.ClaimsPrincipal user,
        string action,
        string entityType,
        Guid entityId,
        Guid? clientId = null,
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
        db.DocumentAccessLogs.Add(DocumentAccessLog.Create(
            Guid.NewGuid(),
            document.Id,
            document.ClientId,
            user.GetUserId(),
            actorRole,
            action,
            httpContext.Connection.RemoteIpAddress?.ToString(),
            httpContext.Request.Headers.UserAgent.ToString(),
            metadataJson));

        await db.SaveChangesAsync(ct);
    }
}
