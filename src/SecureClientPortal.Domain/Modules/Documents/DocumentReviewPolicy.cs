using SecureClientPortal.Backend.Models;
using SecureClientPortal.Backend.Domain.Modules.MonthlyPacks;

namespace SecureClientPortal.Backend.Domain.Modules.Documents;

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
        DateTime decidedAtUtc,
        IReadOnlyCollection<Document>? linkedDocuments = null)
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

        var aggregate = slot is not null && linkedDocuments is { Count: > 1 };
        var targetSlot = aggregate ? null : slot;
        switch (normalizedDecision)
        {
            case "under_review":
                document.MarkUnderReview();
                targetSlot?.MarkUnderReview();
                pack?.MarkUnderReview();
                break;
            case "accepted":
                document.Accept();
                if (targetSlot is not null)
                {
                    targetSlot.Accept(document.Id);
                }
                break;
            case "rejected":
            case "request_reupload":
                document.Reject();
                if (targetSlot is not null)
                {
                    targetSlot.RequestReupload(document.Id, reason);
                }
                break;
            default:
                throw new DomainRuleException("Unsupported document lifecycle decision.");
        }

        if(aggregate)slot!.RecalculateReview(linkedDocuments!);
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
