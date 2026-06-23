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
        var hasClientFilter = Guid.TryParse(clientId, out var parsedClientId);

        var query = _db.AuditLogs.AsQueryable();

        if (user.IsAdmin())
        {
            if (hasClientFilter)
            {
                query = query.Where(x => x.ClientId == parsedClientId);
            }
        }
        else
        {
            query = query.Where(x => x.ClientId.HasValue && allowedClientIds.Contains(x.ClientId.Value));
            if (!string.IsNullOrWhiteSpace(clientId))
            {
                if (!hasClientFilter || !allowedClientIds.Contains(parsedClientId))
                {
                    return (true, []);
                }
                query = query.Where(x => x.ClientId == parsedClientId);
            }
        }

        var data = await query.OrderByDescending(x => x.CreatedAtUtc).Take(cappedLimit).ToListAsync(ct);
        return (false, data);
    }
}
