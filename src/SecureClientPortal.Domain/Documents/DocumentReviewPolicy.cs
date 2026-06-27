namespace SecureClientPortal.Backend.Models;

public static class DocumentReviewPolicy
{
    public static ReviewDecision ApplyDecision(
        Document document,
        DocumentSlot? slot,
        MonthlyPack? pack,
        string decision,
        Guid reviewerUserId,
        string reviewerRole,
        string? reason,
        string? internalNote,
        DateTime decidedAtUtc)
    {
        var normalizedDecision = NormalizeDecision(decision);
        var reviewDecision = ReviewDecision.Create(
            Guid.NewGuid(),
            document.Id,
            normalizedDecision,
            reviewerUserId,
            reviewerRole,
            reason,
            internalNote,
            decidedAtUtc);

        switch (normalizedDecision)
        {
            case "under_review":
                document.MarkUnderReview();
                slot?.MarkUnderReview();
                pack?.MarkUnderReview();
                break;
            case "accepted":
                document.Accept();
                if (slot is not null)
                {
                    slot.Accept(document.Id);
                }
                break;
            case "rejected":
            case "request_reupload":
                document.Reject();
                if (slot is not null)
                {
                    slot.Reject(document.Id);
                }
                pack?.Reopen();
                break;
            default:
                throw new DomainRuleException("Unsupported document lifecycle decision.");
        }

        return reviewDecision;
    }

    public static string NormalizeDecision(string decision)
    {
        var normalized = string.IsNullOrWhiteSpace(decision)
            ? string.Empty
            : decision.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');

        return normalized switch
        {
            "under_review" => "under_review",
            "accepted" => "accepted",
            "rejected" => "rejected",
            "request_reupload" => "request_reupload",
            _ => throw new DomainRuleException("Unsupported document lifecycle decision.")
        };
    }
}
