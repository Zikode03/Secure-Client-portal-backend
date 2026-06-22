namespace SecureClientPortal.Backend.Application.Platform;

public interface IHealthService
{
    object GetServiceHealth();
    Task<(bool ok, string database, string? error)> GetDatabaseHealthAsync(CancellationToken ct = default);
}
