namespace SecureClientPortal.Backend.Models;

public class ComplianceReminder
{
    public string Id { get; set; } = string.Empty;
    public string ComplianceItemId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string RecipientUserId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = "pending";
    public DateTime ScheduledForUtc { get; set; }
    public DateTime? SentAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
