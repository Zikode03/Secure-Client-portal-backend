namespace SecureClientPortal.Backend.Models;

public class ReviewDecision
{
    public string Id { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public string Decision { get; set; } = string.Empty;
    public string ReviewerUserId { get; set; } = string.Empty;
    public string ReviewerRole { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public DateTime DecidedAtUtc { get; set; } = DateTime.UtcNow;
}
