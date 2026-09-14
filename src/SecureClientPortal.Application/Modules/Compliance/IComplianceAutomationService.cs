using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts.Modules.Compliance;
using System.Security.Claims;

namespace SecureClientPortal.Backend.Application.Modules.Compliance;

public interface IComplianceAutomationService
{
    Task<ServiceResult<ComplianceRuleSetDto>> GetRulesAsync(ClaimsPrincipal user, CancellationToken ct = default);
    Task<ServiceResult<ComplianceRuleSetDto>> UpdateRulesAsync(UpdateComplianceRuleSetRequest request, ClaimsPrincipal user, CancellationToken ct = default);
    Task<ServiceResult<ClientComplianceProfileDto>> GetProfileAsync(Guid clientId, ClaimsPrincipal user, CancellationToken ct = default);
    Task<ServiceResult<ClientComplianceProfileDto>> UpdateProfileAsync(Guid clientId, UpdateClientComplianceProfileRequest request, ClaimsPrincipal user, CancellationToken ct = default);
    Task<ServiceResult<IReadOnlyList<ComplianceObligationDto>>> GetObligationsAsync(ClaimsPrincipal user, Guid? clientId = null, CancellationToken ct = default);
    Task<ServiceResult<ComplianceObligationDto>> RecordPreparationAsync(Guid obligationId, RecordCompliancePreparationRequest request, ClaimsPrincipal user, CancellationToken ct = default);
    Task<ServiceResult<ComplianceObligationDto>> RecordReviewAsync(Guid obligationId, RecordComplianceReviewRequest request, ClaimsPrincipal user, CancellationToken ct = default);
    Task<ServiceResult<ComplianceObligationDto>> RecordSubmissionAsync(Guid obligationId, RecordComplianceSubmissionRequest request, ClaimsPrincipal user, CancellationToken ct = default);
    Task<ServiceResult<ComplianceObligationDto>> RecordPaymentAsync(Guid obligationId, RecordCompliancePaymentRequest request, ClaimsPrincipal user, CancellationToken ct = default);
    Task<ServiceResult<ComplianceObligationDto>> MarkNotApplicableAsync(Guid obligationId, MarkComplianceNotApplicableRequest request, ClaimsPrincipal user, CancellationToken ct = default);
    Task<ServiceResult<ComplianceAutomationRunResult>> RunAsync(ClaimsPrincipal user, Guid? clientId = null, DateTime? utcNow = null, CancellationToken ct = default);
    Task<ComplianceAutomationRunResult> RunSystemAsync(DateTime? utcNow = null, CancellationToken ct = default);
}
