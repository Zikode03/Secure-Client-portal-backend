using System.Security.Claims;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts.Modules.Compliance;

namespace SecureClientPortal.Backend.Application.Modules.Compliance;

public interface IComplianceMonitoringService
{
    Task<ServiceResult<ComplianceMonitoringResponse>> GetAsync(Guid clientId, ClaimsPrincipal user, CancellationToken ct);
    Task<ServiceResult<ComplianceMonitoringResponse>> UpdateAsync(Guid clientId, UpdateComplianceMonitoringRequest request, ClaimsPrincipal user, CancellationToken ct);
    Task<ServiceResult<ComplianceMonitoringResponse>> RecordManualAsync(Guid clientId, RecordManualVerificationRequest request, ClaimsPrincipal user, CancellationToken ct);
    Task<ServiceResult<IReadOnlyList<VerificationResponse>>> HistoryAsync(Guid clientId, string? checkCode, int page, ClaimsPrincipal user, CancellationToken ct);
}
