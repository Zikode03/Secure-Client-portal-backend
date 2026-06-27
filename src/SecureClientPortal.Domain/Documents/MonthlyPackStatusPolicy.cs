namespace SecureClientPortal.Backend.Models;

public static class MonthlyPackStatusPolicy
{
    public static void Recalculate(MonthlyPack pack, IReadOnlyCollection<DocumentSlot> slots)
    {
        if (slots.Count == 0)
        {
            pack.MarkDraft();
            return;
        }

        if (slots.Where(x => x.IsRequired).All(x => x.Status == DocumentSlotStatus.Accepted.ToStorageValue()))
        {
            pack.Complete();
        }
        else if (slots.Any(x => x.Status == DocumentSlotStatus.Rejected.ToStorageValue()))
        {
            pack.Reopen();
        }
        else if (slots.Any(x => x.Status == DocumentSlotStatus.UnderReview.ToStorageValue()))
        {
            pack.MarkUnderReview();
        }
        else if (slots.Any(x => x.Status == DocumentSlotStatus.Uploaded.ToStorageValue()))
        {
            if (pack.SubmittedAtUtc.HasValue)
            {
                pack.MarkSubmitted(pack.SubmittedAtUtc);
            }
            else
            {
                pack.MarkInProgress();
            }
        }
        else if (slots.Any(x => x.Status is "accepted" or "rejected" or "filed"))
        {
            pack.MarkInProgress();
        }
        else
        {
            pack.MarkDraft();
        }
    }
}
