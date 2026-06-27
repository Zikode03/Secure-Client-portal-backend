using System.ComponentModel.DataAnnotations.Schema;

namespace SecureClientPortal.Backend.Models;

public class Document : IHasDomainEvents
{
    [NotMapped]
    private readonly List<IDomainEvent> _domainEvents = [];

    public Guid Id { get; private set; }
    public Guid ClientId { get; private set; }
    public Guid MonthlyPackId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Category { get; private set; } = "general";
    public Guid? DocumentSlotId { get; private set; }
    public string Status { get; private set; } = DocumentStatus.Uploaded.ToStorageValue();
    public string FileType { get; private set; } = "application/octet-stream";
    public long SizeBytes { get; private set; }
    public string? StorageKey { get; private set; }
    public Guid UploadedByUserId { get; private set; }
    public int CurrentVersionNumber { get; private set; } = 1;
    public bool IsFiled { get; private set; }
    public DateTime? FiledAtUtc { get; private set; }
    public Guid? FiledByUserId { get; private set; }
    public DateTime UploadedAtUtc { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; private set; } = DateTime.UtcNow;

    public static Document CreateUploaded(Guid id, Guid clientId, Guid monthlyPackId, string name, string category, Guid? documentSlotId, string fileType, long sizeBytes, string? storageKey, Guid uploadedByUserId)
    {
        if (id == Guid.Empty) throw new DomainRuleException("Document id is required.");
        if (clientId == Guid.Empty) throw new DomainRuleException("Client id is required.");

        var document = new Document
        {
            Id = id,
            ClientId = clientId
        };

        document.ApplyUpload(monthlyPackId, name, category, documentSlotId, fileType, sizeBytes, storageKey, uploadedByUserId, 1);
        return document;
    }

    public void ReplaceUpload(Guid monthlyPackId, string name, string category, Guid? documentSlotId, string fileType, long sizeBytes, string? storageKey, Guid uploadedByUserId)
    {
        ApplyUpload(monthlyPackId, name, category, documentSlotId, fileType, sizeBytes, storageKey, uploadedByUserId, CurrentVersionNumber + 1);
    }

    public void UpdateMetadata(string name, string category, DocumentStatus status, long sizeBytes, string? storageKey)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainRuleException("Document name is required.");
        if (sizeBytes < 0) throw new DomainRuleException("Document size cannot be negative.");

        Name = name.Trim();
        Category = DocumentDomainValues.NormalizeCategory(category);
        Status = status.ToStorageValue();
        SizeBytes = sizeBytes;
        StorageKey = string.IsNullOrWhiteSpace(storageKey) ? null : storageKey.Trim();
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

    public void File(Guid filedByUserId)
    {
        if (filedByUserId == Guid.Empty) throw new DomainRuleException("Filing user id is required.");

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

    public void RecordReviewDecision(string decision, string? reason, Guid reviewerUserId, string reviewerRole, DateTime occurredAtUtc)
    {
        _domainEvents.Add(new DocumentReviewedDomainEvent(
            Id,
            ClientId,
            Name,
            decision,
            reason,
            reviewerUserId,
            reviewerRole,
            occurredAtUtc));
    }

    private void ApplyUpload(Guid monthlyPackId, string name, string category, Guid? documentSlotId, string fileType, long sizeBytes, string? storageKey, Guid uploadedByUserId, int versionNumber)
    {
        if (monthlyPackId == Guid.Empty) throw new DomainRuleException("Monthly pack id is required.");
        if (string.IsNullOrWhiteSpace(name)) throw new DomainRuleException("Document name is required.");
        if (string.IsNullOrWhiteSpace(fileType)) throw new DomainRuleException("File type is required.");
        if (sizeBytes < 0) throw new DomainRuleException("Document size cannot be negative.");
        if (uploadedByUserId == Guid.Empty) throw new DomainRuleException("Uploading user id is required.");

        MonthlyPackId = monthlyPackId;
        Name = name.Trim();
        Category = DocumentDomainValues.NormalizeCategory(category);
        DocumentSlotId = documentSlotId == Guid.Empty ? null : documentSlotId;
        Status = DocumentStatus.Uploaded.ToStorageValue();
        FileType = fileType.Trim();
        SizeBytes = sizeBytes;
        StorageKey = string.IsNullOrWhiteSpace(storageKey) ? null : storageKey.Trim();
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

    public IReadOnlyCollection<IDomainEvent> DequeueDomainEvents()
    {
        var events = _domainEvents.ToArray();
        _domainEvents.Clear();
        return events;
    }
}
