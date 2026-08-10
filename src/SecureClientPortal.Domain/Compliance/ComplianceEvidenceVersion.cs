namespace SecureClientPortal.Backend.Models;

public class ComplianceEvidenceVersion
{
    public Guid Id { get; private set; }
    public Guid ComplianceItemId { get; private set; }
    public Guid ClientId { get; private set; }
    public int VersionNumber { get; private set; }
    public string FileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = "application/octet-stream";
    public long SizeBytes { get; private set; }
    public string StorageKey { get; private set; } = string.Empty;
    public Guid UploadedByUserId { get; private set; }
    public string? Note { get; private set; }
    public bool IsCurrentVersion { get; private set; }
    public DateTime UploadedAtUtc { get; private set; } = DateTime.UtcNow;

    public static ComplianceEvidenceVersion Create(
        Guid id,
        Guid complianceItemId,
        Guid clientId,
        int versionNumber,
        string fileName,
        string contentType,
        long sizeBytes,
        string storageKey,
        Guid uploadedByUserId,
        string? note,
        DateTime? uploadedAtUtc = null)
    {
        if (id == Guid.Empty) throw new DomainRuleException("Evidence version id is required.");
        if (complianceItemId == Guid.Empty) throw new DomainRuleException("Compliance item id is required.");
        if (clientId == Guid.Empty) throw new DomainRuleException("Client id is required.");
        if (versionNumber < 1) throw new DomainRuleException("Evidence version number must be positive.");
        if (string.IsNullOrWhiteSpace(fileName)) throw new DomainRuleException("Evidence file name is required.");
        if (string.IsNullOrWhiteSpace(storageKey)) throw new DomainRuleException("Evidence storage key is required.");
        if (uploadedByUserId == Guid.Empty) throw new DomainRuleException("Uploading user id is required.");

        return new ComplianceEvidenceVersion
        {
            Id = id,
            ComplianceItemId = complianceItemId,
            ClientId = clientId,
            VersionNumber = versionNumber,
            FileName = fileName.Trim(),
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType.Trim(),
            SizeBytes = Math.Max(0, sizeBytes),
            StorageKey = storageKey.Trim(),
            UploadedByUserId = uploadedByUserId,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            IsCurrentVersion = true,
            UploadedAtUtc = uploadedAtUtc ?? DateTime.UtcNow
        };
    }

    public void MarkNotCurrent() => IsCurrentVersion = false;
}
