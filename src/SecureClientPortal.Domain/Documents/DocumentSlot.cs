namespace SecureClientPortal.Backend.Models;

public class DocumentSlot
{
    public Guid Id { get; private set; }
    public Guid MonthlyPackId { get; private set; }
    public Guid ClientId { get; private set; }
    public string Category { get; private set; } = string.Empty;
    public string Label { get; private set; } = string.Empty;
    public bool IsRequired { get; private set; } = true;
    public string Status { get; private set; } = DocumentSlotStatus.Missing.ToStorageValue();
    public Guid? CurrentDocumentId { get; private set; }
    public DateTime? DueDateUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; private set; } = DateTime.UtcNow;

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

    public void MarkUploaded(Guid documentId)
    {
        if (documentId == Guid.Empty) throw new DomainRuleException("Document id is required.");
        CurrentDocumentId = documentId;
        Status = DocumentSlotStatus.Uploaded.ToStorageValue();
        Touch();
    }

    public void MarkUnderReview()
    {
        Status = DocumentSlotStatus.UnderReview.ToStorageValue();
        Touch();
    }

    public void Accept(Guid documentId)
    {
        if (documentId == Guid.Empty) throw new DomainRuleException("Document id is required.");
        CurrentDocumentId = documentId;
        Status = DocumentSlotStatus.Accepted.ToStorageValue();
        Touch();
    }

    public void Reject(Guid documentId)
    {
        if (documentId == Guid.Empty) throw new DomainRuleException("Document id is required.");
        CurrentDocumentId = documentId;
        Status = DocumentSlotStatus.Rejected.ToStorageValue();
        Touch();
    }

    public void MarkMissing()
    {
        CurrentDocumentId = null;
        Status = DocumentSlotStatus.Missing.ToStorageValue();
        Touch();
    }

    private void Touch()
    {
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
