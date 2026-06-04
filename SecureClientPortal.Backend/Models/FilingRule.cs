namespace SecureClientPortal.Backend.Models;

/// <summary>
/// Defines which document categories are eligible for automatic filing.
/// </summary>
public class FilingRule
{
    public string Id { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

