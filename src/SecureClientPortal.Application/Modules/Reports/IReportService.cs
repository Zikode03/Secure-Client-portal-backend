using System.Security.Claims;

namespace SecureClientPortal.Backend.Application.Modules.Reports;

public interface IReportService
{
    Task<(bool forbidden, object? report)> GetFirmReportsAsync(ClaimsPrincipal user, CancellationToken ct = default);
    Task<(bool forbidden, object? report)> GetOperationsDashboardAsync(ClaimsPrincipal user, CancellationToken ct = default);
    Task<object> GetAccountantReportsAsync(CancellationToken ct = default);
    Task<object> GetClientReportsAsync(ClaimsPrincipal user, CancellationToken ct = default);
}
