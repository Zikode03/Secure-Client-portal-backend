namespace SecureClientPortal.Backend.Domain.Shared.Modules.MonthlyPacks;

public enum DocumentSlotStatus { NotStarted, Draft, Submitted, UnderReview, Accepted, Rejected, ReuploadRequired, NotApplicable }
public enum MonthlyPackStatus { NotStarted, InProgress, PartiallySubmitted, UnderReview, Complete, Closed }

public static class MonthlyPackDomainValues
{
    public static string ToStorageValue(this DocumentSlotStatus status) => status switch
    {
        DocumentSlotStatus.NotStarted => "not_started",
        DocumentSlotStatus.Draft => "draft",
        DocumentSlotStatus.Submitted => "submitted",
        DocumentSlotStatus.UnderReview => "under_review",
        DocumentSlotStatus.Accepted => "accepted",
        DocumentSlotStatus.Rejected => "rejected",
        DocumentSlotStatus.ReuploadRequired => "reupload_required",
        DocumentSlotStatus.NotApplicable => "not_applicable",
        _ => "draft"
    };

    public static DocumentSlotStatus ToDocumentSlotStatus(string raw) => (raw?.Trim().ToLowerInvariant()) switch
    {
        "not_started" => DocumentSlotStatus.NotStarted,
        "missing" => DocumentSlotStatus.NotStarted,
        "draft" => DocumentSlotStatus.Draft,
        "uploaded" => DocumentSlotStatus.Draft,
        "submitted" => DocumentSlotStatus.Submitted,
        "under_review" => DocumentSlotStatus.UnderReview,
        "accepted" => DocumentSlotStatus.Accepted,
        "rejected" => DocumentSlotStatus.Rejected,
        "reupload_required" => DocumentSlotStatus.ReuploadRequired,
        "not_applicable" => DocumentSlotStatus.NotApplicable,
        "filed" => DocumentSlotStatus.Accepted,
        _ => DocumentSlotStatus.NotStarted
    };

    public static string ToStorageValue(this MonthlyPackStatus status) => status switch
    {
        MonthlyPackStatus.NotStarted => "not_started",
        MonthlyPackStatus.PartiallySubmitted => "partially_submitted",
        MonthlyPackStatus.UnderReview => "under_review",
        MonthlyPackStatus.Complete => "complete",
        MonthlyPackStatus.Closed => "closed",
        _ => "in_progress"
    };

    public static MonthlyPackStatus ToMonthlyPackStatus(string raw) => (raw?.Trim().ToLowerInvariant()) switch
    {
        "not_started" => MonthlyPackStatus.NotStarted,
        "draft" => MonthlyPackStatus.NotStarted,
        "partially_submitted" => MonthlyPackStatus.PartiallySubmitted,
        "submitted" => MonthlyPackStatus.PartiallySubmitted,
        "under_review" => MonthlyPackStatus.UnderReview,
        "complete" => MonthlyPackStatus.Complete,
        "completed" => MonthlyPackStatus.Complete,
        "closed" => MonthlyPackStatus.Closed,
        _ => MonthlyPackStatus.InProgress
    };
}
