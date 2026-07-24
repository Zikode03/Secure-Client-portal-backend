using SecureClientPortal.Backend.Models;
using SecureClientPortal.Backend.Domain.Shared.Modules.Documents;
using SecureClientPortal.Backend.Domain.Shared.Modules.MonthlyPacks;

namespace SecureClientPortal.Backend.Domain.Modules.MonthlyPacks;

public class DocumentSlot
{
    public Guid Id { get; private set; }
    public Guid MonthlyPackId { get; private set; }
    public Guid ClientId { get; private set; }
    public string Category { get; private set; } = string.Empty;
    public string Label { get; private set; } = string.Empty;
    public bool IsRequired { get; private set; } = true;
    public string Status { get; private set; } = DocumentSlotStatus.NotStarted.ToStorageValue();
    public Guid? CurrentDocumentId { get; private set; }
    public DateTime? DueDateUtc { get; private set; }
    public DateTime? SubmittedAtUtc { get; private set; }
    public Guid? SubmittedByUserId { get; private set; }
    public string? RejectionReason { get; private set; }
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; private set; } = DateTime.UtcNow;
    public bool CanCurrentlyBeSubmitted =>
        CurrentDocumentId.HasValue &&
        Status is "draft" or "reupload_required" or "rejected";
    public string ReviewStatus => Status switch
    {
        "submitted" => "submitted",
        "under_review" => "under_review",
        "accepted" => "accepted",
        "rejected" => "rejected",
        "reupload_required" => "reupload_required",
        _ => "pending"
    };

    public static DocumentSlot Create(Guid id, Guid monthlyPackId, Guid clientId, string category, string label, bool isRequired, DateTime? dueDateUtc, DateTime? createdAtUtc = null)
    {
        if (id == Guid.Empty) throw new DomainRuleException("Document slot id is required.");
        if (monthlyPackId == Guid.Empty) throw new DomainRuleException("Monthly pack id is required.");
        if (clientId == Guid.Empty) throw new DomainRuleException("Client id is required.");

        var created = createdAtUtc ?? DateTime.UtcNow;
        var slot = new DocumentSlot
        {
            Id = id,
            MonthlyPackId = monthlyPackId,
            ClientId = clientId,
            CreatedAtUtc = created,
            UpdatedAtUtc = created,
            DueDateUtc = dueDateUtc
        };

        slot.UpdateDefinition(category, label, isRequired);
        return slot;
    }

    public void UpdateDefinition(string category, string label, bool isRequired)
    {
        if (string.IsNullOrWhiteSpace(label)) throw new DomainRuleException("Document slot label is required.");
        Category = DocumentDomainValues.NormalizeCategory(category);
        Label = label.Trim();
        IsRequired = isRequired;
        Touch();
    }

    public void UpdateSchedule(DateTime? dueDateUtc)
    {
        DueDateUtc = dueDateUtc;
        Touch();
    }

    public void MarkDraft(Guid documentId)
    {
        if (documentId == Guid.Empty) throw new DomainRuleException("Document id is required.");
        CurrentDocumentId = documentId;
        Status = DocumentSlotStatus.Draft.ToStorageValue();
        SubmittedAtUtc = null;
        SubmittedByUserId = null;
        RejectionReason = null;
        Touch();
    }

    public void Submit(Guid submittedByUserId, DateTime? submittedAtUtc = null)
    {
        if (submittedByUserId == Guid.Empty) throw new DomainRuleException("Submitted by user id is required.");
        if (!CurrentDocumentId.HasValue) throw new DomainRuleException("A slot can only be submitted when it has an uploaded current document.");
        if (Status == DocumentSlotStatus.NotApplicable.ToStorageValue()) throw new DomainRuleException("Not applicable slots cannot be submitted.");
        if (Status is "submitted" or "under_review") throw new DomainRuleException("This slot is already awaiting review.");
        if (Status == DocumentSlotStatus.Accepted.ToStorageValue()) throw new DomainRuleException("Accepted slots do not need to be re-submitted.");

        Status = DocumentSlotStatus.Submitted.ToStorageValue();
        SubmittedAtUtc = submittedAtUtc ?? DateTime.UtcNow;
        SubmittedByUserId = submittedByUserId;
        RejectionReason = null;
        Touch(SubmittedAtUtc.Value);
    }

    public void MarkUnderReview()
    {
        if (Status != DocumentSlotStatus.Submitted.ToStorageValue())
        {
            throw new DomainRuleException("Only submitted slots can move into review.");
        }

        Status = DocumentSlotStatus.UnderReview.ToStorageValue();
        Touch();
    }

    public void Accept(Guid documentId)
    {
        if (documentId == Guid.Empty) throw new DomainRuleException("Document id is required.");
        if (Status is not ("submitted" or "under_review"))
        {
            throw new DomainRuleException("Only submitted or in-review slots can be accepted.");
        }

        CurrentDocumentId = documentId;
        Status = DocumentSlotStatus.Accepted.ToStorageValue();
        RejectionReason = null;
        Touch();
    }

    public void Reject(Guid documentId, string? reason = null)
    {
        if (documentId == Guid.Empty) throw new DomainRuleException("Document id is required.");
        if (Status is not ("submitted" or "under_review"))
        {
            throw new DomainRuleException("Only submitted or in-review slots can be rejected.");
        }

        CurrentDocumentId = documentId;
        Status = DocumentSlotStatus.Rejected.ToStorageValue();
        RejectionReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        Touch();
    }

    public void RequestReupload(Guid documentId, string? reason = null)
    {
        if (documentId == Guid.Empty) throw new DomainRuleException("Document id is required.");
        if (Status is not ("submitted" or "under_review"))
        {
            throw new DomainRuleException("Only submitted or in-review slots can request a re-upload.");
        }

        CurrentDocumentId = documentId;
        Status = DocumentSlotStatus.ReuploadRequired.ToStorageValue();
        RejectionReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        SubmittedAtUtc = null;
        SubmittedByUserId = null;
        Touch();
    }

    public void MarkNotStarted()
    {
        CurrentDocumentId = null;
        Status = DocumentSlotStatus.NotStarted.ToStorageValue();
        SubmittedAtUtc = null;
        SubmittedByUserId = null;
        RejectionReason = null;
        Touch();
    }

    public void MarkNotApplicable()
    {
        CurrentDocumentId = null;
        Status = DocumentSlotStatus.NotApplicable.ToStorageValue();
        SubmittedAtUtc = null;
        SubmittedByUserId = null;
        RejectionReason = null;
        Touch();
    }

    private void Touch(DateTime? timestamp = null)
    {
        UpdatedAtUtc = timestamp ?? DateTime.UtcNow;
    }
}
