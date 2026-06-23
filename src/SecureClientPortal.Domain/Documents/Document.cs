namespace SecureClientPortal.Backend.Models;

public class Document
{
    public string Id { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string MonthlyPackId { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Category { get; private set; } = "general";
    public string? DocumentSlotId { get; private set; }
    public string Status { get; private set; } = DocumentStatus.Uploaded.ToStorageValue();
    public string FileType { get; private set; } = "application/octet-stream";
    public long SizeBytes { get; private set; }
    public string? StorageKey { get; private set; }
    public string UploadedByUserId { get; private set; } = string.Empty;
    public int CurrentVersionNumber { get; private set; } = 1;
    public bool IsFiled { get; private set; }
    public DateTime? FiledAtUtc { get; private set; }
    public string? FiledByUserId { get; private set; }
    public DateTime UploadedAtUtc { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; private set; } = DateTime.UtcNow;

    public static Document CreateUploaded(string id, string clientId, string monthlyPackId, string name, string category, string? documentSlotId, string fileType, long sizeBytes, string? storageKey, string uploadedByUserId)
    {
        var document = new Document { Id = id, ClientId = clientId };
        document.ApplyUpload(monthlyPackId, name, category, documentSlotId, fileType, sizeBytes, storageKey, uploadedByUserId, 1);
        return document;
    }

    public void ReplaceUpload(string monthlyPackId, string name, string category, string? documentSlotId, string fileType, long sizeBytes, string? storageKey, string uploadedByUserId)
    {
        ApplyUpload(monthlyPackId, name, category, documentSlotId, fileType, sizeBytes, storageKey, uploadedByUserId, CurrentVersionNumber + 1);
    }

    public void UpdateMetadata(string name, string category, DocumentStatus status, long sizeBytes, string? storageKey)
    {
        Name = name;
        Category = DocumentDomainValues.NormalizeCategory(category);
        Status = status.ToStorageValue();
        SizeBytes = sizeBytes;
        StorageKey = storageKey;
        Touch();
    }

    public void MarkUnderReview()
    {
        Status = DocumentStatus.UnderReview.ToStorageValue();
        Touch();
    }

    public void Accept()
    {
        Status = DocumentStatus.Accepted.ToStorageValue();
        ClearFiling();
        Touch();
    }

    public void Reject()
    {
        Status = DocumentStatus.Rejected.ToStorageValue();
        ClearFiling();
        Touch();
    }

    public void File(string filedByUserId)
    {
        Status = DocumentStatus.Filed.ToStorageValue();
        IsFiled = true;
        FiledAtUtc = DateTime.UtcNow;
        FiledByUserId = filedByUserId;
        Touch();
    }

    public void ClearFiling()
    {
        IsFiled = false;
        FiledAtUtc = null;
        FiledByUserId = null;
    }

    private void ApplyUpload(string monthlyPackId, string name, string category, string? documentSlotId, string fileType, long sizeBytes, string? storageKey, string uploadedByUserId, int versionNumber)
    {
        MonthlyPackId = monthlyPackId;
        Name = name;
        Category = DocumentDomainValues.NormalizeCategory(category);
        DocumentSlotId = string.IsNullOrWhiteSpace(documentSlotId) ? null : documentSlotId;
        Status = DocumentStatus.Uploaded.ToStorageValue();
        FileType = fileType;
        SizeBytes = sizeBytes;
        StorageKey = storageKey;
        UploadedByUserId = uploadedByUserId;
        CurrentVersionNumber = versionNumber;
        ClearFiling();
        UploadedAtUtc = DateTime.UtcNow;
        Touch(UploadedAtUtc);
    }

    private void Touch(DateTime? timestamp = null)
    {
        UpdatedAtUtc = timestamp ?? DateTime.UtcNow;
    }
}
