namespace SecureClientPortal.Backend.Application.Contracts.Modules.Compliance;

public sealed record CheckApplicabilityRequest(string CheckCode, string Applicability, string Reason);
public sealed record UpdateComplianceMonitoringRequest(Guid Version, string RegistrationNumber, string TaxNumber,
    string CsdSupplierNumber, IReadOnlyList<CheckApplicabilityRequest> Checks);
public sealed record RecordManualVerificationRequest(Guid Version, string CheckCode, string Outcome,
    string EvidenceReference, DateTime CheckedAtUtc, DateTime ReviewAfterUtc);
public sealed record RunAuthorityVerificationRequest(Guid Version, string CheckCode);
public sealed record VerificationResponse(Guid Id, string CheckCode, string Method, string Outcome,
    string EvidenceReference, DateTime CheckedAtUtc, DateTime RecordedAtUtc, DateTime ReviewAfterUtc,
    Guid RecordedByUserId, string RecordedByName, bool MatchesCurrentIdentifiers);
public sealed record MonitoringCheckResponse(string Code, string Source, string Name, string Description,
    string Applicability, string Reason, string ConnectionStatus, string VerificationStatus,
    VerificationResponse? LatestVerification);
public sealed record ComplianceMonitoringResponse(Guid ClientId, string ClientName, Guid Version,
    string RegistrationNumber, string TaxNumber, string CsdSupplierNumber, bool CanManage,
    IReadOnlyList<MonitoringCheckResponse> Checks);
