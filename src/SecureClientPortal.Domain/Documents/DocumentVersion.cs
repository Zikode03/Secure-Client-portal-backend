namespace SecureClientPortal.Backend.Models;

public class DocumentVersion
{
    public Guid Id { get; private set; }
    public Guid DocumentId { get; private set; }
    public int VersionNumber { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string OriginalFileName { get; private set; } = string.Empty;
    public string StoredFileName { get; private set; } = string.Empty;
    public string FileType { get; private set; } = "application/octet-stream";
    public long SizeBytes { get; private set; }
    public string? StorageKey { get; private set; }
    public bool IsCurrentVersion { get; private set; } = true;
    public Guid UploadedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;

    public static DocumentVersion Create(
        Guid id,
        Guid documentId,
        int versionNumber,
        string name,
        string originalFileName,
        string storedFileName,
        string fileType,
        long sizeBytes,
        string? storageKey,
        bool isCurrentVersion,
        Guid uploadedByUserId,
        DateTime? createdAtUtc = null)
    {
        return new DocumentVersion
        {
            Id = id,
            DocumentId = documentId == Guid.Empty ? throw new ArgumentException("Document id is required.", nameof(documentId)) : documentId,
            VersionNumber = versionNumber,
            Name = string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("Version name is required.", nameof(name)) : name.Trim(),
            OriginalFileName = string.IsNullOrWhiteSpace(originalFileName) ? throw new ArgumentException("Original file name is required.", nameof(originalFileName)) : originalFileName.Trim(),
            StoredFileName = string.IsNullOrWhiteSpace(storedFileName) ? throw new ArgumentException("Stored file name is required.", nameof(storedFileName)) : storedFileName.Trim(),
            FileType = string.IsNullOrWhiteSpace(fileType) ? "application/octet-stream" : fileType.Trim(),
            SizeBytes = sizeBytes,
            StorageKey = string.IsNullOrWhiteSpace(storageKey) ? null : storageKey.Trim(),
            IsCurrentVersion = isCurrentVersion,
            UploadedByUserId = uploadedByUserId == Guid.Empty ? throw new ArgumentException("Uploaded by user id is required.", nameof(uploadedByUserId)) : uploadedByUserId,
            CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow
        };
    }

    public void MarkNotCurrent()
    {
        IsCurrentVersion = false;
    }
}






