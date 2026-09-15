namespace SecureClientPortal.Backend.Application.Contracts.Modules.Compliance;

public record ComplianceRuleDefinitionDto(
    string Code,
    string Name,
    string Authority,
    string CategoryCode,
    int CadenceMonths,
    int DueOffsetMonths,
    int? DueDayOfMonth,
    string ApplicabilityField,
    bool RequiresSubmission,
    bool RequiresPayment,
    IReadOnlyList<string> RequiredDocumentCategories,
    DateTime EffectiveFromUtc,
    DateTime? EffectiveToUtc,
    string Version);

public record ComplianceRuleSetDto(
    string Version,
    IReadOnlyList<ComplianceRuleDefinitionDto> Rules,
    DateTime UpdatedAtUtc);

public record UpdateComplianceRuleSetRequest(
    string Version,
    IReadOnlyList<ComplianceRuleDefinitionDto> Rules);

public record ClientComplianceProfileDto(
    Guid ClientId,
    bool? VatRegistered,
    int VatCycleMonths,
    int VatAnchorMonth,
    bool? PayeRegistered,
    bool? UifRegistered,
    bool? CoidaRegistered,
    bool? ProvisionalTaxpayer,
    bool? CompanyTaxRegistered,
    bool? CipcRegistered,
    bool? GovernmentSupplier,
    bool? CsdRegistered,
    string? CsdSupplierNumber,
    int FinancialYearEndMonth,
    DateTime UpdatedAtUtc);

public record UpdateClientComplianceProfileRequest(
    bool? VatRegistered,
    int VatCycleMonths = 2,
    int VatAnchorMonth = 1,
    bool? PayeRegistered = null,
    bool? UifRegistered = null,
    bool? CoidaRegistered = null,
    bool? ProvisionalTaxpayer = null,
    bool? CompanyTaxRegistered = null,
    bool? CipcRegistered = null,
    bool? GovernmentSupplier = null,
    bool? CsdRegistered = null,
    string? CsdSupplierNumber = null,
    int FinancialYearEndMonth = 2);

public record ComplianceObligationDto(
    Guid Id,
    Guid ClientId,
    string ClientName,
    string Code,
    string Name,
    string Authority,
    DateTime PeriodStartUtc,
    DateTime PeriodEndUtc,
    DateTime? DueDateUtc,
    string WorkflowStatus,
    string Readiness,
    string PreparationStatus,
    string ReviewStatus,
    string SubmissionStatus,
    DateTime? SubmittedAtUtc,
    string? SubmissionReference,
    decimal? AmountPayable,
    decimal? AmountRefundable,
    bool PaymentRequired,
    string PaymentStatus,
    DateTime? PaidAtUtc,
    string? PaymentReference,
    int EvidenceRequired,
    int EvidenceFound,
    IReadOnlyList<string> MissingEvidenceCategories,
    Guid? ResponsibleAccountantId,
    string RuleVersion,
    string CreatedReason,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public record RecordCompliancePreparationRequest(bool Complete, string? Note = null);
public record RecordComplianceReviewRequest(bool Approved, string? Note = null);
public record RecordComplianceSubmissionRequest(
    DateTime SubmittedAtUtc,
    string SubmissionReference,
    decimal? AmountPayable,
    decimal? AmountRefundable,
    bool PaymentRequired,
    string? Note = null);
public record RecordCompliancePaymentRequest(
    DateTime PaidAtUtc,
    string PaymentReference,
    decimal AmountPaid,
    string? Note = null);
public record MarkComplianceNotApplicableRequest(string Reason);
