using SecureClientPortal.Backend.Models;
using System.Security.Claims;

namespace SecureClientPortal.Backend.Application.Modules.AuditLogs;

public interface IAuditLogService
{
    Task<(bool forbidden, IReadOnlyList<AuditLog> items)> GetAllAsync(ClaimsPrincipal user, string? clientId = null, int limit = 200, CancellationToken ct = default);
}
