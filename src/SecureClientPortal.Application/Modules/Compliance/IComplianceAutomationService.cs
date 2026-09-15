using System.Security.Claims;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts.Modules.Compliance;

namespace SecureClientPortal.Backend.Application.Modules.Compliance;

public interface IComplianceAutomationService
{
    Task<ServiceResult<ComplianceRuleSet>> GetRulesAsync(ClaimsPrincipal user, CancellationToken ct);
    Task<ServiceResult<ComplianceRuleSet>> UpdateRulesAsync(UpdateComplianceRulesRequest request, ClaimsPrincipal user, CancellationToken ct);
    Task<ServiceResult<ClientComplianceProfile>> GetProfileAsync(Guid clientId, ClaimsPrincipal user, CancellationToken ct);
    Task<ServiceResult<ClientComplianceProfile>> UpdateProfileAsync(Guid clientId, ClientComplianceProfile request, ClaimsPrincipal user, CancellationToken ct);
    Task<ServiceResult<IReadOnlyList<ComplianceObligationResponse>>> GetObligationsAsync(Guid? clientId, ClaimsPrincipal user, CancellationToken ct);
    Task<ServiceResult<ComplianceAutomationRunResult>> RunAsync(Guid? clientId, ClaimsPrincipal user, CancellationToken ct);
    Task<ServiceResult<ComplianceObligationResponse>> PreparationAsync(Guid id, PreparationRequest request, ClaimsPrincipal user, CancellationToken ct);
    Task<ServiceResult<ComplianceObligationResponse>> ReviewAsync(Guid id, ReviewObligationRequest request, ClaimsPrincipal user, CancellationToken ct);
    Task<ServiceResult<ComplianceObligationResponse>> SubmissionAsync(Guid id, SubmissionRequest request, ClaimsPrincipal user, CancellationToken ct);
    Task<ServiceResult<ComplianceObligationResponse>> PaymentAsync(Guid id, PaymentRequest request, ClaimsPrincipal user, CancellationToken ct);
    Task<ServiceResult<ComplianceObligationResponse>> NotApplicableAsync(Guid id, NotApplicableRequest request, ClaimsPrincipal user, CancellationToken ct);
    Task<ServiceResult<ObligationEvidenceResponse>> UploadEvidenceAsync(Guid id, UploadComplianceEvidenceRequest request, ClaimsPrincipal user, CancellationToken ct);
    Task<ServiceResult<IReadOnlyList<ComplianceEvidenceVersionResponse>>> GetEvidenceAsync(Guid id, ClaimsPrincipal user, CancellationToken ct);
}

