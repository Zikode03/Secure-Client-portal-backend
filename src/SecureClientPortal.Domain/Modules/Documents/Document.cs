using System.ComponentModel.DataAnnotations.Schema;
using SecureClientPortal.Backend.Models;
using SecureClientPortal.Backend.Domain.Modules.Documents.Events;
using SecureClientPortal.Backend.Domain.Shared.Modules.Documents;

namespace SecureClientPortal.Backend.Domain.Modules.Documents;

public class Document : IHasDomainEvents
{
    [NotMapped]
    private readonly List<IDomainEvent> _domainEvents = [];

    public Guid Id { get; private set; }
    public Guid ClientId { get; private set; }
    public Guid? MonthlyPackId { get; private set; }
    [NotMapped] public string ArchiveScope { get; private set; } = "monthly";
    [NotMapped] public int? ArchiveYear { get; private set; }
    [NotMapped] public int? ArchiveMonth { get; private set; }
    public void RestoreArchivePlacement(string scope,int? year,int? month) {
        if(scope is not ("monthly" or "annual" or "permanent" or "uncertain"))throw new DomainRuleException("Choose a supported archive section.");
        if(scope is "monthly" or "annual" && (year is null or <1900 or >9998))throw new DomainRuleException("Choose a valid record year.");
        if(scope=="monthly" && month is null or <1 or >12)throw new DomainRuleException("Choose a valid record month.");
        ArchiveScope=scope;ArchiveYear=scope is "permanent" or "uncertain"?null:year;ArchiveMonth=scope=="monthly"?month:null;
    }
    public void SetArchiveMonthlyPack(Guid? packId) {
        if(DocumentSlotId.HasValue)throw new DomainRuleException("Correct the monthly checklist association before changing the archive period.");
        MonthlyPackId=packId;Touch();
    }
    public string Name { get; private set; } = string.Empty;
    [System.ComponentModel.DataAnnotations.MaxLength(64)]
    public string? ContentSha256 { get; private set; }
    public void SetContentSha256(string hash) => ContentSha256 = hash;
    public bool InvoiceReviewRequired { get; private set; }
    public int InvoiceReviewRevision { get; private set; }
    public void RecordInvoiceReview()
    {
        InvoiceReviewRequired = true;
        InvoiceReviewRevision++;
        Touch();
    }
    public string? InvoiceDetailsJson { get; private set; }
    public void SetInvoiceDetails(string? json) => InvoiceDetailsJson = json;

    public string Category { get; private set; } = "general";
    public Guid? DocumentSlotId { get; private set; }
    public string Status { get; private set; } = DocumentStatus.Uploaded.ToStorageValue();
    public string FileType { get; private set; } = "application/octet-stream";
    public long SizeBytes { get; private set; }
    public string? StorageKey { get; private set; }
    public Guid UploadedByUserId { get; private set; }
    public int CurrentVersionNumber { get; private set; } = 1;
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public bool IsHistoricallyFiled { get; private set; }
    public void RestoreHistoricalFilingProvenance() => IsHistoricallyFiled = IsFiled && CurrentVersionNumber == 1;
    public bool IsFiled { get; private set; }
    public DateTime? FiledAtUtc { get; private set; }
    public Guid? FiledByUserId { get; private set; }
    public DateTime UploadedAtUtc { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; private set; } = DateTime.UtcNow;

    public static Document CreateUploaded(Guid id, Guid clientId, Guid? monthlyPackId, string name, string category, Guid? documentSlotId, string fileType, long sizeBytes, string? storageKey, Guid uploadedByUserId)
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

    public void ReplaceUpload(Guid? monthlyPackId, string name, string category, Guid? documentSlotId, string fileType, long sizeBytes, string? storageKey, Guid uploadedByUserId)
    {
        ApplyUpload(monthlyPackId, name, category, documentSlotId, fileType, sizeBytes, storageKey, uploadedByUserId, CurrentVersionNumber + 1);
    }

    public void UpdateMetadata(string name, string category, DocumentStatus status, long sizeBytes, string? storageKey)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainRuleException("Document name is required.");
        if (sizeBytes < 0) throw new DomainRuleException("Document size cannot be negative.");

        Name = name.Trim();
        Category = DocumentDomainValues.NormalizeCategory(category);
        InvoiceReviewRequired |= Category.Contains("invoice", StringComparison.OrdinalIgnoreCase);
        Status = status.ToStorageValue();
        SizeBytes = sizeBytes;
        StorageKey = string.IsNullOrWhiteSpace(storageKey) ? null : storageKey.Trim();
        Touch();
    }

    public void LinkToChecklist(Guid slotId, string category)
    {
        if(slotId==Guid.Empty)throw new DomainRuleException("A checklist item is required.");
        if(DocumentSlotId.HasValue&&DocumentSlotId!=slotId)throw new DomainRuleException("This document already belongs to another checklist item.");
        if(IsFiled||Status is not ("uploaded" or "draft"))throw new DomainRuleException("Only unreviewed uploads can be linked to a checklist item.");
        DocumentSlotId=slotId;
        Category=DocumentDomainValues.NormalizeCategory(category);
        InvoiceReviewRequired |= Category.Contains("invoice",StringComparison.OrdinalIgnoreCase);
        Touch();
    }

    // An import attestation is not a review performed inside this system.
    public void ConfirmHistoricalFiling(Guid confirmedByUserId)
    {
        EnsureStatusIsOneOf("Only a new upload can receive a historical filing attestation.", DocumentStatus.Uploaded);
        if (confirmedByUserId == Guid.Empty) throw new DomainRuleException("A confirming accountant is required.");
        Status = DocumentStatus.Filed.ToStorageValue();
        IsFiled = true;
        FiledAtUtc = DateTime.UtcNow;
        FiledByUserId = confirmedByUserId;
        InvoiceReviewRequired = false;
        IsHistoricallyFiled = true;
        Touch();
    }

    public void MarkUnderReview()
    {
        EnsureStatusIsOneOf(
            "Only uploaded documents can enter review.",
            DocumentStatus.Uploaded);
        Status = DocumentStatus.UnderReview.ToStorageValue();
        Touch();
    }

    public void Accept()
    {
        EnsureStatusIsOneOf(
            "Only uploaded or in-review documents can be accepted.",
            DocumentStatus.Uploaded,
            DocumentStatus.UnderReview);
        Status = DocumentStatus.Accepted.ToStorageValue();
        ClearFiling();
        Touch();
    }

    public void Reject()
    {
        EnsureStatusIsOneOf(
            "Only uploaded or in-review documents can be rejected.",
            DocumentStatus.Uploaded,
            DocumentStatus.UnderReview);
        Status = DocumentStatus.Rejected.ToStorageValue();
        ClearFiling();
        Touch();
    }

    public void File(Guid filedByUserId)
    {
        if (filedByUserId == Guid.Empty) throw new DomainRuleException("Filing user id is required.");
        EnsureStatusIsOneOf(
            "Only accepted documents can be filed.",
            DocumentStatus.Accepted);

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

    private void ApplyUpload(Guid? monthlyPackId, string name, string category, Guid? documentSlotId, string fileType, long sizeBytes, string? storageKey, Guid uploadedByUserId, int versionNumber)
    {
        if (monthlyPackId == Guid.Empty || monthlyPackId is null && documentSlotId.HasValue) throw new DomainRuleException("A monthly checklist document requires a monthly pack.");
        if (string.IsNullOrWhiteSpace(name)) throw new DomainRuleException("Document name is required.");
        if (string.IsNullOrWhiteSpace(fileType)) throw new DomainRuleException("File type is required.");
        if (sizeBytes < 0) throw new DomainRuleException("Document size cannot be negative.");
        if (uploadedByUserId == Guid.Empty) throw new DomainRuleException("Uploading user id is required.");

        MonthlyPackId = monthlyPackId;
        Name = name.Trim();
        Category = DocumentDomainValues.NormalizeCategory(category);
        InvoiceReviewRequired |= Category.Contains("invoice", StringComparison.OrdinalIgnoreCase);
        DocumentSlotId = documentSlotId == Guid.Empty ? null : documentSlotId;
        Status = DocumentStatus.Uploaded.ToStorageValue();
        FileType = fileType.Trim();
        SizeBytes = sizeBytes;
        StorageKey = string.IsNullOrWhiteSpace(storageKey) ? null : storageKey.Trim();
        ContentSha256 = null;
        UploadedByUserId = uploadedByUserId;
        CurrentVersionNumber = versionNumber;
        ClearFiling();
        UploadedAtUtc = DateTime.UtcNow;
        Touch(UploadedAtUtc);
    }

    private void EnsureStatusIsOneOf(string error, params DocumentStatus[] allowedStatuses)
    {
        var currentStatus = DocumentDomainValues.ToDocumentStatus(Status);
        if (!allowedStatuses.Contains(currentStatus))
        {
            throw new DomainRuleException(error);
        }
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
