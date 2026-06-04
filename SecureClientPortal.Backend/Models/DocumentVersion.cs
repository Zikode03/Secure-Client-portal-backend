namespace SecureClientPortal.Backend.Models;

public class DocumentVersion
{
    public string Id { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public int VersionNumber { get; set; }
    public string Name { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string? StorageKey { get; set; }
    public string UploadedByUserId { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
