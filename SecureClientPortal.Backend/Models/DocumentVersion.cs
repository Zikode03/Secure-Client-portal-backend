namespace SecureClientPortal.Backend.Models;

public class DocumentVersion
{
    public string Id { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public int VersionNumber { get; set; }
    public string Name { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string StoredFileName { get; set; } = string.Empty;
    public string FileType { get; set; } = "application/octet-stream";
    public long SizeBytes { get; set; }
    public string? StorageKey { get; set; }
    public bool IsCurrentVersion { get; set; } = true;
    public string UploadedByUserId { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
