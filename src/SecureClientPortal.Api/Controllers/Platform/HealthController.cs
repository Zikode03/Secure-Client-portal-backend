using Microsoft.AspNetCore.Mvc;
using SecureClientPortal.Backend.Application.Platform;

namespace SecureClientPortal.Backend.Controllers;

[ApiController]
[Route("health")]
public class HealthController : ControllerBase
{
    private readonly IHealthService _healthService;

    public HealthController(IHealthService healthService)
    {
        _healthService = healthService;
    }

    [HttpGet]
    public IActionResult GetServiceHealth() => Ok(_healthService.GetServiceHealth());

    [HttpGet("db")]
    public async Task<IActionResult> GetDatabaseHealth(CancellationToken ct)
    {
        var result = await _healthService.GetDatabaseHealthAsync(ct);
        if (result.ok)
        {
            return Ok(new { ok = result.ok, database = result.database });
        }

        return StatusCode(503, new { ok = result.ok, database = result.database, error = result.error });
    }
}
