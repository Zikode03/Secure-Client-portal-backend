namespace SecureClientPortal.Backend.Models;

public enum RequestStatus { Open, WaitingOnClient, WaitingOnAccountant, Resolved, Overdue }
public enum RequestPriority { Low, Medium, High, Urgent }

public static class RequestDomainValues
{
    public static string ToStorageValue(this RequestStatus status) => status switch
    {
        RequestStatus.WaitingOnClient => "waiting_on_client",
        RequestStatus.WaitingOnAccountant => "waiting_on_accountant",
        RequestStatus.Resolved => "resolved",
        RequestStatus.Overdue => "overdue",
        _ => "open"
    };

    public static RequestStatus ToRequestStatus(string raw) => (raw?.Trim().ToLowerInvariant().Replace("-", "_").Replace(" ", "_")) switch
    {
        "waiting_on_client" or "awaiting_client" => RequestStatus.WaitingOnClient,
        "waiting_on_accountant" or "awaiting_accountant" => RequestStatus.WaitingOnAccountant,
        "resolved" => RequestStatus.Resolved,
        "overdue" => RequestStatus.Overdue,
        _ => RequestStatus.Open
    };

    public static string NormalizeRequestType(string value)
    {
        var normalized = value.Trim().ToLowerInvariant().Replace("-", "_").Replace(" ", "_");
        return normalized switch
        {
            "reupload" => "reupload_required",
            "re_upload" => "reupload_required",
            "clarification" => "clarification_needed",
            "signature" => "signature_required",
            "renewal" => "compliance_renewal",
            _ => normalized
        };
    }

    public static string ToStorageValue(this RequestPriority priority) => priority switch
    {
        RequestPriority.Low => "low",
        RequestPriority.High => "high",
        RequestPriority.Urgent => "urgent",
        _ => "medium"
    };

    public static RequestPriority ToRequestPriority(string raw) => (raw?.Trim().ToLowerInvariant()) switch
    {
        "low" => RequestPriority.Low,
        "high" => RequestPriority.High,
        "urgent" => RequestPriority.Urgent,
        _ => RequestPriority.Medium
    };
}
