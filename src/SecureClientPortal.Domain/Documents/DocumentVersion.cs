namespace SecureClientPortal.Backend.Models;

public class DocumentVersion
{
    public string Id { get; private set; } = string.Empty;
    public string DocumentId { get; private set; } = string.Empty;
    public int VersionNumber { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string OriginalFileName { get; private set; } = string.Empty;
    public string StoredFileName { get; private set; } = string.Empty;
    public string FileType { get; private set; } = "application/octet-stream";
    public long SizeBytes { get; private set; }
    public string? StorageKey { get; private set; }
    public bool IsCurrentVersion { get; private set; } = true;
    public string UploadedByUserId { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;

    public static DocumentVersion Create(
        string id,
        string documentId,
        int versionNumber,
        string name,
        string originalFileName,
        string storedFileName,
        string fileType,
        long sizeBytes,
        string? storageKey,
        bool isCurrentVersion,
        string uploadedByUserId,
        DateTime? createdAtUtc = null)
    {
        return new DocumentVersion
        {
            Id = id,
            DocumentId = string.IsNullOrWhiteSpace(documentId) ? throw new ArgumentException("Document id is required.", nameof(documentId)) : documentId.Trim(),
            VersionNumber = versionNumber,
            Name = string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("Version name is required.", nameof(name)) : name.Trim(),
            OriginalFileName = string.IsNullOrWhiteSpace(originalFileName) ? throw new ArgumentException("Original file name is required.", nameof(originalFileName)) : originalFileName.Trim(),
            StoredFileName = string.IsNullOrWhiteSpace(storedFileName) ? throw new ArgumentException("Stored file name is required.", nameof(storedFileName)) : storedFileName.Trim(),
            FileType = string.IsNullOrWhiteSpace(fileType) ? "application/octet-stream" : fileType.Trim(),
            SizeBytes = sizeBytes,
            StorageKey = string.IsNullOrWhiteSpace(storageKey) ? null : storageKey.Trim(),
            IsCurrentVersion = isCurrentVersion,
            UploadedByUserId = string.IsNullOrWhiteSpace(uploadedByUserId) ? throw new ArgumentException("Uploaded by user id is required.", nameof(uploadedByUserId)) : uploadedByUserId.Trim(),
            CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow
        };
    }

    public void MarkNotCurrent()
    {
        IsCurrentVersion = false;
    }
}
