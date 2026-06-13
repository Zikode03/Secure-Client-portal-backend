namespace SecureClientPortal.Backend.Models;

public class DocumentAccessLog
{
    public string Id { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string? AccessedByUserId { get; set; }
    public string AccessedByRole { get; set; } = "unknown";
    public string Action { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? MetadataJson { get; set; }
    public DateTime AccessedAtUtc { get; set; } = DateTime.UtcNow;
}
