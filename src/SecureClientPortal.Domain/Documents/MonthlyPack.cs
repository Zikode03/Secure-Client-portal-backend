namespace SecureClientPortal.Backend.Models;

public class MonthlyPack
{
    public string Id { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public int Year { get; set; }
    public int Month { get; set; }
    public string Status { get; private set; } = MonthlyPackStatus.Draft.ToStorageValue();
    public DateTime? SubmittedAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; private set; } = DateTime.UtcNow;

    public void MarkDraft()
    {
        Status = MonthlyPackStatus.Draft.ToStorageValue();
        SubmittedAtUtc = null;
        Touch();
    }

    public void MarkInProgress(bool preserveSubmission = false)
    {
        Status = MonthlyPackStatus.InProgress.ToStorageValue();
        if (!preserveSubmission) SubmittedAtUtc = null;
        Touch();
    }

    public void MarkSubmitted()
    {
        Status = MonthlyPackStatus.Submitted.ToStorageValue();
        SubmittedAtUtc ??= DateTime.UtcNow;
        Touch(SubmittedAtUtc.Value);
    }

    public void MarkUnderReview()
    {
        Status = MonthlyPackStatus.UnderReview.ToStorageValue();
        SubmittedAtUtc ??= DateTime.UtcNow;
        Touch();
    }

    public void Reopen()
    {
        Status = MonthlyPackStatus.Reopened.ToStorageValue();
        SubmittedAtUtc = null;
        Touch();
    }

    public void Complete()
    {
        Status = MonthlyPackStatus.Completed.ToStorageValue();
        Touch();
    }

    private void Touch(DateTime? timestamp = null)
    {
        UpdatedAtUtc = timestamp ?? DateTime.UtcNow;
    }
}
