using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Models;

namespace SecureClientPortal.Backend.Controllers;

[ApiController]
[Route("api/audit-logs")]
[Authorize(Policy = "AccountantOnly")]
public class AuditLogsController : ControllerBase
{
    private readonly PortalDbContext _db;

    public AuditLogsController(PortalDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<AuditLog>>> GetAll([FromQuery] string? clientId = null, [FromQuery] int limit = 200)
    {
        var allowedClientIds = await User.GetAccessibleClientIdsAsync(_db);
        var cappedLimit = Math.Clamp(limit, 1, 500);

        var query = _db.AuditLogs.AsQueryable();

        if (User.IsAdmin())
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
                    return Forbid();
                }
                query = query.Where(x => x.ClientId == clientId);
            }
        }

        var data = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(cappedLimit)
            .ToListAsync();

        return Ok(data);
    }
}
