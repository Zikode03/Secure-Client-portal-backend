namespace SecureClientPortal.Backend.Models;

public class MonthlyPack
{
    public Guid Id { get; private set; }
    public Guid ClientId { get; private set; }
    public int Year { get; private set; }
    public int Month { get; private set; }
    public string Status { get; private set; } = MonthlyPackStatus.Draft.ToStorageValue();
    public DateTime? SubmittedAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; private set; } = DateTime.UtcNow;

    public static MonthlyPack Create(Guid id, Guid clientId, int year, int month, DateTime? createdAtUtc = null)
    {
        if (id == Guid.Empty) throw new DomainRuleException("Monthly pack id is required.");
        if (clientId == Guid.Empty) throw new DomainRuleException("Client id is required.");
        if (month is < 1 or > 12) throw new DomainRuleException("Month must be between 1 and 12.");
        if (year < 2000) throw new DomainRuleException("Year is invalid.");

        var created = createdAtUtc ?? DateTime.UtcNow;
        return new MonthlyPack
        {
            Id = id,
            ClientId = clientId,
            Year = year,
            Month = month,
            CreatedAtUtc = created,
            UpdatedAtUtc = created,
            Status = MonthlyPackStatus.Draft.ToStorageValue()
        };
    }

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

    public void MarkSubmitted(DateTime? submittedAtUtc = null)
    {
        Status = MonthlyPackStatus.Submitted.ToStorageValue();
        SubmittedAtUtc ??= submittedAtUtc ?? DateTime.UtcNow;
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
