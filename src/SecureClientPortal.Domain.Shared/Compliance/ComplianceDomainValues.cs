namespace SecureClientPortal.Backend.Models;

public enum ComplianceItemStatus { Missing, Pending, Valid, ExpiringSoon, Expired, Rejected }
public enum ComplianceRiskLevel { Low, Medium, High, Critical }
public enum ComplianceReminderStatus { Pending, Sent, Dismissed }

public static class ComplianceDomainValues
{
    public static string ToStorageValue(this ComplianceItemStatus status) => status switch
    {
        ComplianceItemStatus.Pending => "pending",
        ComplianceItemStatus.Valid => "valid",
        ComplianceItemStatus.ExpiringSoon => "expiring_soon",
        ComplianceItemStatus.Expired => "expired",
        ComplianceItemStatus.Rejected => "rejected",
        _ => "missing"
    };

    public static ComplianceItemStatus ToComplianceItemStatus(string raw) => (raw?.Trim().ToLowerInvariant()) switch
    {
        "pending" => ComplianceItemStatus.Pending,
        "valid" => ComplianceItemStatus.Valid,
        "expiring_soon" => ComplianceItemStatus.ExpiringSoon,
        "expired" => ComplianceItemStatus.Expired,
        "rejected" => ComplianceItemStatus.Rejected,
        _ => ComplianceItemStatus.Missing
    };

    public static string ToStorageValue(this ComplianceRiskLevel riskLevel) => riskLevel switch
    {
        ComplianceRiskLevel.Low => "low",
        ComplianceRiskLevel.High => "high",
        ComplianceRiskLevel.Critical => "critical",
        _ => "medium"
    };

    public static ComplianceRiskLevel ToComplianceRiskLevel(string raw) => (raw?.Trim().ToLowerInvariant()) switch
    {
        "low" => ComplianceRiskLevel.Low,
        "high" => ComplianceRiskLevel.High,
        "critical" => ComplianceRiskLevel.Critical,
        _ => ComplianceRiskLevel.Medium
    };

    public static string ToStorageValue(this ComplianceReminderStatus status) => status switch
    {
        ComplianceReminderStatus.Sent => "sent",
        ComplianceReminderStatus.Dismissed => "dismissed",
        _ => "pending"
    };

    public static ComplianceReminderStatus ToComplianceReminderStatus(string raw) => (raw?.Trim().ToLowerInvariant()) switch
    {
        "sent" => ComplianceReminderStatus.Sent,
        "dismissed" => ComplianceReminderStatus.Dismissed,
        _ => ComplianceReminderStatus.Pending
    };

    public static string NormalizeDocumentCategory(string raw) => raw.Trim().ToLowerInvariant().Replace("-", "_").Replace(" ", "_");
}
