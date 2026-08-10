using System.Security.Claims;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts.Modules.Reports;

namespace SecureClientPortal.Backend.Application.Modules.Reports;

public interface IReportService
{
    Task<(bool forbidden, object? report)> GetFirmReportsAsync(ClaimsPrincipal user, CancellationToken ct = default);
    Task<(bool forbidden, object? report)> GetOperationsDashboardAsync(ClaimsPrincipal user, CancellationToken ct = default);
    Task<object> GetAccountantReportsAsync(CancellationToken ct = default);
    Task<object> GetClientReportsAsync(ClaimsPrincipal user, CancellationToken ct = default);
    Task<ServiceResult<ReportFileResponse>> GenerateCompliancePdfAsync(ClaimsPrincipal user, string? clientId = null, CancellationToken ct = default);
    Task<ServiceResult<IReadOnlyList<ReportScheduleResponse>>> GetSchedulesAsync(ClaimsPrincipal user, string? clientId = null, CancellationToken ct = default);
    Task<ServiceResult<ReportScheduleResponse>> CreateScheduleAsync(CreateReportScheduleRequest request, ClaimsPrincipal user, CancellationToken ct = default);
    Task<ServiceResult<ReportScheduleResponse>> UpdateScheduleAsync(string id, UpdateReportScheduleRequest request, ClaimsPrincipal user, CancellationToken ct = default);
    Task<ServiceResult<bool>> DeleteScheduleAsync(string id, ClaimsPrincipal user, CancellationToken ct = default);
}
