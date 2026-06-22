using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application.Platform;
using SecureClientPortal.Backend.Data;

namespace SecureClientPortal.Backend.Infrastructure.Platform;

public sealed class HealthService : IHealthService
{
    private readonly PortalDbContext _db;

    public HealthService(PortalDbContext db)
    {
        _db = db;
    }

    public object GetServiceHealth() => new { ok = true, service = "secure-client-portal-backend" };

    public async Task<(bool ok, string database, string? error)> GetDatabaseHealthAsync(CancellationToken ct = default)
    {
        try
        {
            await _db.Database.ExecuteSqlRawAsync("SELECT 1", ct);
            return (true, "sqlserver", null);
        }
        catch (Exception ex)
        {
            return (false, "sqlserver", ex.Message);
        }
    }
}
