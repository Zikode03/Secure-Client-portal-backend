namespace SecureClientPortal.Backend.Application.Contracts.Modules.Compliance;

public sealed record ComplianceRuleDefinition(
    string Code, string Name, string Authority, string CategoryCode,
    int CadenceMonths, int DueOffsetMonths, int? DueDayOfMonth, string ApplicabilityField,
    bool RequiresSubmission, bool RequiresPayment, string[] RequiredDocumentCategories,
    DateTime EffectiveFromUtc, DateTime? EffectiveToUtc, string Version);
public sealed record ComplianceRuleSet(string Version, ComplianceRuleDefinition[] Rules, DateTime UpdatedAtUtc);
public sealed record UpdateComplianceRulesRequest(string Version, ComplianceRuleDefinition[] Rules);

public sealed record ClientComplianceProfile
{
    public Guid ClientId { get; init; }
    public bool? VatRegistered { get; init; }
    public int VatCycleMonths { get; init; } = 2;
    public int VatAnchorMonth { get; init; } = 2;
    public bool? PayeRegistered { get; init; }
    public bool? UifRegistered { get; init; }
    public bool? CoidaRegistered { get; init; }
    public bool? ProvisionalTaxpayer { get; init; }
    public bool? CompanyTaxRegistered { get; init; }
    public bool? CipcRegistered { get; init; }
    public bool? GovernmentSupplier { get; init; }
    public bool? CsdRegistered { get; init; }
    public string? CsdSupplierNumber { get; init; }
    public int FinancialYearEndMonth { get; init; } = 2;
    public DateTime? UpdatedAtUtc { get; init; }
}

public sealed record ComplianceObligationResponse
{
    public Guid Id { get; init; }
    public Guid ClientId { get; init; }
    public string ClientName { get; init; } = "";
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public string Authority { get; init; } = "";
    public DateTime PeriodStartUtc { get; init; }
    public DateTime PeriodEndUtc { get; init; }
    public DateTime? DueDateUtc { get; init; }
    public string WorkflowStatus { get; init; } = "waiting_for_client";
    public string Readiness { get; init; } = "waiting_for_client";
    public string PreparationStatus { get; init; } = "not_started";
    public string ReviewStatus { get; init; } = "not_started";
    public string SubmissionStatus { get; init; } = "not_submitted";
    public DateTime? SubmittedAtUtc { get; init; }
    public string? SubmissionReference { get; init; }
    public decimal? AmountPayable { get; init; }
    public decimal? AmountRefundable { get; init; }
    public bool PaymentRequired { get; init; }
    public string PaymentStatus { get; init; } = "not_required";
    public DateTime? PaidAtUtc { get; init; }
    public string? PaymentReference { get; init; }
    public decimal? AmountPaid { get; init; }
    public int EvidenceRequired { get; init; }
    public int EvidenceFound { get; init; }
    public string[] MissingEvidenceCategories { get; init; } = [];
    public Guid? ResponsibleAccountantId { get; init; }
    public string RuleVersion { get; init; } = "";
    public string CreatedReason { get; init; } = "";
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
    public string? NotApplicableReason { get; init; }
}
public sealed record ComplianceAutomationRunResult(DateTime RunAtUtc, int ClientsEvaluated,
    int ObligationsCreated, int ObligationsRefreshed, int MissingEvidenceRequestsCreated,
    int NotificationsCreated, string[] Warnings);
public sealed record PreparationRequest(bool Complete, string? Note);
public sealed record ReviewObligationRequest(bool Approved, string? Note);
public sealed record SubmissionRequest(DateTime SubmittedAtUtc, string SubmissionReference,
    decimal? AmountPayable, decimal? AmountRefundable, bool PaymentRequired, string? Note);
public sealed record PaymentRequest(DateTime PaidAtUtc, string PaymentReference, decimal AmountPaid, string? Note);
public sealed record NotApplicableRequest(string Reason);
public sealed record ObligationEvidenceResponse(ComplianceObligationResponse Obligation, ComplianceEvidenceVersionResponse Evidence);
