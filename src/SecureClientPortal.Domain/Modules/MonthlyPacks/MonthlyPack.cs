using SecureClientPortal.Backend.Models;
using SecureClientPortal.Backend.Domain.Shared.Modules.MonthlyPacks;

namespace SecureClientPortal.Backend.Domain.Modules.MonthlyPacks;

public class MonthlyPack
{
    public Guid Id { get; private set; }
    public Guid ClientId { get; private set; }
    public int Year { get; private set; }
    public int Month { get; private set; }
    public string Status { get; private set; } = MonthlyPackStatus.NotStarted.ToStorageValue();
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
            Status = MonthlyPackStatus.NotStarted.ToStorageValue()
        };
    }

    public void MarkNotStarted()
    {
        Status = MonthlyPackStatus.NotStarted.ToStorageValue();
        Touch();
    }

    public void MarkInProgress()
    {
        Status = MonthlyPackStatus.InProgress.ToStorageValue();
        Touch();
    }

    public void MarkPartiallySubmitted()
    {
        Status = MonthlyPackStatus.PartiallySubmitted.ToStorageValue();
        Touch();
    }

    public void MarkUnderReview()
    {
        Status = MonthlyPackStatus.UnderReview.ToStorageValue();
        Touch();
    }

    public void Complete()
    {
        Status = MonthlyPackStatus.Complete.ToStorageValue();
        Touch();
    }

    public void Close()
    {
        Status = MonthlyPackStatus.Closed.ToStorageValue();
        Touch();
    }

    private void Touch(DateTime? timestamp = null)
    {
        UpdatedAtUtc = timestamp ?? DateTime.UtcNow;
    }
}
