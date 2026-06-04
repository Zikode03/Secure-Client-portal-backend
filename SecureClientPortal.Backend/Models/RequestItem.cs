namespace SecureClientPortal.Backend.Models;

public class RequestItem
{
    public string Id { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string RequestType { get; set; } = "clarification";
    public string? RelatedDocumentId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Priority { get; set; } = "medium";
    public string Status { get; set; } = "open";
    public DateTime? DueDateUtc { get; set; }
    public string RequestedByUserId { get; set; } = string.Empty;
    public string? ResolvedByUserId { get; set; }
    public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
