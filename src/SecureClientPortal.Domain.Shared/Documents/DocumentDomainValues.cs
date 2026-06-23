namespace SecureClientPortal.Backend.Models;

public enum DocumentStatus { Draft, Uploaded, UnderReview, Accepted, Rejected, Filed }
public enum DocumentSlotStatus { Missing, Uploaded, UnderReview, Accepted, Rejected, Filed }
public enum MonthlyPackStatus { Draft, InProgress, Submitted, UnderReview, Reopened, Completed }

public static class DocumentDomainValues
{
    public static string ToStorageValue(this DocumentStatus status) => status switch
    {
        DocumentStatus.Draft => "draft",
        DocumentStatus.UnderReview => "under_review",
        DocumentStatus.Accepted => "accepted",
        DocumentStatus.Rejected => "rejected",
        DocumentStatus.Filed => "filed",
        _ => "uploaded"
    };

    public static DocumentStatus ToDocumentStatus(string raw) => (raw?.Trim().ToLowerInvariant()) switch
    {
        "draft" => DocumentStatus.Draft,
        "under_review" => DocumentStatus.UnderReview,
        "accepted" => DocumentStatus.Accepted,
        "rejected" => DocumentStatus.Rejected,
        "filed" => DocumentStatus.Filed,
        _ => DocumentStatus.Uploaded
    };

    public static string NormalizeCategory(string value)
    {
        var raw = value.Trim().ToLowerInvariant().Replace("-", "_").Replace(" ", "_");
        return raw switch
        {
            "bankstatement" => "bank_statement",
            "bank_statement" => "bank_statement",
            "invoice" => "invoices",
            "invoices" => "invoices",
            "signeddocuments" => "signed_documents",
            "signed_documents" => "signed_documents",
            "compliancerecord" => "compliance_record",
            "compliance_record" => "compliance_record",
            "payrollsummary" => "payroll_summary",
            "payroll_summary" => "payroll_summary",
            "taxworkingpapers" => "tax_working_papers",
            "tax_working_papers" => "tax_working_papers",
            "proofofpayment" => "proof_of_payment",
            "proof_of_payment" => "proof_of_payment",
            "creditnotes" => "credit_notes",
            "credit_notes" => "credit_notes",
            "debitnotes" => "debit_notes",
            "debit_notes" => "debit_notes",
            _ => raw
        };
    }

    public static string ToStorageValue(this DocumentSlotStatus status) => status switch
    {
        DocumentSlotStatus.Missing => "missing",
        DocumentSlotStatus.UnderReview => "under_review",
        DocumentSlotStatus.Accepted => "accepted",
        DocumentSlotStatus.Rejected => "rejected",
        DocumentSlotStatus.Filed => "filed",
        _ => "uploaded"
    };

    public static DocumentSlotStatus ToDocumentSlotStatus(string raw) => (raw?.Trim().ToLowerInvariant()) switch
    {
        "missing" => DocumentSlotStatus.Missing,
        "under_review" => DocumentSlotStatus.UnderReview,
        "accepted" => DocumentSlotStatus.Accepted,
        "rejected" => DocumentSlotStatus.Rejected,
        "filed" => DocumentSlotStatus.Filed,
        _ => DocumentSlotStatus.Uploaded
    };

    public static string ToStorageValue(this MonthlyPackStatus status) => status switch
    {
        MonthlyPackStatus.Draft => "draft",
        MonthlyPackStatus.Submitted => "submitted",
        MonthlyPackStatus.UnderReview => "under_review",
        MonthlyPackStatus.Reopened => "reopened",
        MonthlyPackStatus.Completed => "completed",
        _ => "in_progress"
    };

    public static MonthlyPackStatus ToMonthlyPackStatus(string raw) => (raw?.Trim().ToLowerInvariant()) switch
    {
        "draft" => MonthlyPackStatus.Draft,
        "submitted" => MonthlyPackStatus.Submitted,
        "under_review" => MonthlyPackStatus.UnderReview,
        "reopened" => MonthlyPackStatus.Reopened,
        "completed" => MonthlyPackStatus.Completed,
        _ => MonthlyPackStatus.InProgress
    };
}
