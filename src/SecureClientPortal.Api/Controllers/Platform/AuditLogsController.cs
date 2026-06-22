using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureClientPortal.Backend.Application.Platform;
using SecureClientPortal.Backend.Models;

namespace SecureClientPortal.Backend.Controllers;

[ApiController]
[Route("api/audit-logs")]
[Authorize(Policy = "AccountantOnly")]
public class AuditLogsController : ControllerBase
{
    private readonly IAuditLogService _auditLogService;

    public AuditLogsController(IAuditLogService auditLogService)
    {
        _auditLogService = auditLogService;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<AuditLog>>> GetAll([FromQuery] string? clientId = null, [FromQuery] int limit = 200, CancellationToken ct = default)
    {
        var result = await _auditLogService.GetAllAsync(User, clientId, limit, ct);
        if (result.forbidden)
        {
            return Forbid();
        }

        return Ok(result.items);
    }
}
