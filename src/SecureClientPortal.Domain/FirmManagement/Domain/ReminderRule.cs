namespace SecureClientPortal.Backend.Models;

public class ReminderRule
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string TriggerType { get; set; } = string.Empty;
    public int DaysBeforeDue { get; set; }
    public string AudienceRole { get; set; } = "client";
    public string MessageTemplate { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
