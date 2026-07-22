using SecureClientPortal.Backend.Domain.Shared.Modules.MonthlyPacks;

namespace SecureClientPortal.Backend.Domain.Modules.MonthlyPacks;

public static class MonthlyPackStatusPolicy
{
    public static void Recalculate(MonthlyPack pack, IReadOnlyCollection<DocumentSlot> slots)
    {
        if (pack.Status == MonthlyPackStatus.Closed.ToStorageValue())
        {
            return;
        }

        var applicableSlots = slots
            .Where(x => x.Status != DocumentSlotStatus.NotApplicable.ToStorageValue())
            .ToArray();

        if (applicableSlots.Length == 0)
        {
            pack.MarkNotStarted();
            return;
        }

        if (applicableSlots.Where(x => x.IsRequired).All(x => x.Status == DocumentSlotStatus.Accepted.ToStorageValue()))
        {
            pack.Complete();
        }
        else if (applicableSlots.Any(x => x.Status == DocumentSlotStatus.UnderReview.ToStorageValue()))
        {
            pack.MarkUnderReview();
        }
        else if (applicableSlots.Any(x => x.Status == DocumentSlotStatus.Submitted.ToStorageValue()))
        {
            pack.MarkPartiallySubmitted();
        }
        else if (applicableSlots.Any(x => x.Status is "draft" or "accepted" or "rejected" or "reupload_required"))
        {
            pack.MarkInProgress();
        }
        else
        {
            pack.MarkNotStarted();
        }
    }
}
