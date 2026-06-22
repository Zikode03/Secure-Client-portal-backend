using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application.Platform;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Models;
using System.Security.Claims;

namespace SecureClientPortal.Backend.Infrastructure.Platform;

public sealed class AuditLogService : IAuditLogService
{
    private readonly PortalDbContext _db;

    public AuditLogService(PortalDbContext db)
    {
        _db = db;
    }

    public async Task<(bool forbidden, IReadOnlyList<AuditLog> items)> GetAllAsync(ClaimsPrincipal user, string? clientId = null, int limit = 200, CancellationToken ct = default)
    {
        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        var cappedLimit = Math.Clamp(limit, 1, 500);

        var query = _db.AuditLogs.AsQueryable();

        if (user.IsAdmin())
        {
            if (!string.IsNullOrWhiteSpace(clientId))
            {
                query = query.Where(x => x.ClientId == clientId);
            }
        }
        else
        {
            query = query.Where(x => x.ClientId != null && allowedClientIds.Contains(x.ClientId));
            if (!string.IsNullOrWhiteSpace(clientId))
            {
                if (!allowedClientIds.Contains(clientId))
                {
                    return (true, []);
                }
                query = query.Where(x => x.ClientId == clientId);
            }
        }

        var data = await query.OrderByDescending(x => x.CreatedAtUtc).Take(cappedLimit).ToListAsync(ct);
        return (false, data);
    }
}
