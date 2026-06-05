namespace SecureClientPortal.Backend.Models;

public class Document
{
    public string Id { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string MonthlyPackId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = "general";
    public string? DocumentSlotId { get; set; }
    public string Status { get; set; } = "uploaded";
    public string FileType { get; set; } = "application/octet-stream";
    public long SizeBytes { get; set; }
    public string? StorageKey { get; set; }
    public string UploadedByUserId { get; set; } = string.Empty;
    public int CurrentVersionNumber { get; set; } = 1;
    // Set when the document enters the immutable filing register.
    public bool IsFiled { get; set; }
    public DateTime? FiledAtUtc { get; set; }
    public string? FiledByUserId { get; set; }
    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
