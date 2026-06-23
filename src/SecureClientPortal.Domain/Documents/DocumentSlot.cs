namespace SecureClientPortal.Backend.Models;

public class DocumentSlot
{
    public string Id { get; set; } = string.Empty;
    public string MonthlyPackId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string Category { get; private set; } = string.Empty;
    public string Label { get; private set; } = string.Empty;
    public bool IsRequired { get; set; } = true;
    public string Status { get; private set; } = DocumentSlotStatus.Missing.ToStorageValue();
    public string? CurrentDocumentId { get; private set; }
    public DateTime? DueDateUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; private set; } = DateTime.UtcNow;

    public void UpdateDefinition(string category, string label, bool isRequired)
    {
        Category = DocumentDomainValues.NormalizeCategory(category);
        Label = label.Trim();
        IsRequired = isRequired;
        Touch();
    }

    public void MarkUploaded(string documentId)
    {
        CurrentDocumentId = documentId;
        Status = DocumentSlotStatus.Uploaded.ToStorageValue();
        Touch();
    }

    public void MarkUnderReview()
    {
        Status = DocumentSlotStatus.UnderReview.ToStorageValue();
        Touch();
    }

    public void Accept(string documentId)
    {
        CurrentDocumentId = documentId;
        Status = DocumentSlotStatus.Accepted.ToStorageValue();
        Touch();
    }

    public void Reject(string documentId)
    {
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
