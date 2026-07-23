using SecureClientPortal.Backend.Domain.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Models;

namespace SecureClientPortal.Backend.Domain.Modules.Documents.Services;

public sealed class DocumentSubmissionDomainService
{
    public void Submit(
        Document document,
        DocumentVersion currentVersion,
        DocumentSlot slot,
        Guid submittedByUserId,
        DateTime submittedAtUtc)
    {
        if (currentVersion.DocumentId != document.Id)
        {
            throw new DomainRuleException("The current version does not belong to the selected document.");
        }

        if (!currentVersion.IsCurrentVersion)
        {
            throw new DomainRuleException("Only the current document version can be submitted.");
        }

        if (slot.CurrentDocumentId != document.Id)
        {
            throw new DomainRuleException("The selected document is not linked to this slot.");
        }

        slot.Submit(submittedByUserId, submittedAtUtc);
    }
}
