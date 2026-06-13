namespace SecureClientPortal.Backend.Models;

public class DeadlineRule
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public int DueDayOfMonth { get; set; }
    public int GraceDays { get; set; }
    public string Priority { get; set; } = "medium";
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
