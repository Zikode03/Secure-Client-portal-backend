using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Data;

namespace SecureClientPortal.Backend.Controllers;

[ApiController]
[Route("health")]
public class HealthController : ControllerBase
{
    private readonly PortalDbContext _db;

    public HealthController(PortalDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public IActionResult GetServiceHealth() => Ok(new { ok = true, service = "secure-client-portal-backend" });

    [HttpGet("db")]
    public async Task<IActionResult> GetDatabaseHealth()
    {
        try
        {
            await _db.Database.ExecuteSqlRawAsync("SELECT 1");
            return Ok(new { ok = true, database = "sqlserver" });
        }
        catch (Exception ex)
        {
            return StatusCode(503, new { ok = false, database = "sqlserver", error = ex.Message });
        }
    }
}
