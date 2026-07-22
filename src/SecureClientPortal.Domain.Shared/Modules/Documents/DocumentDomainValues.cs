namespace SecureClientPortal.Backend.Domain.Shared.Modules.Documents;

public enum DocumentStatus { Draft, Uploaded, UnderReview, Accepted, Rejected, Filed }

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

}
