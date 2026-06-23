namespace SecureClientPortal.Backend.Models;

/// <summary>
/// Defines which document categories are eligible for automatic filing.
/// </summary>
public class FilingRule
{
    public string Id { get; private set; } = string.Empty;
    public string Category { get; private set; } = string.Empty;
    public bool IsEnabled { get; private set; } = true;
    public string Description { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; private set; } = DateTime.UtcNow;

    public static FilingRule Create(
        string id,
        string category,
        string description,
        bool isEnabled,
        DateTime? createdAtUtc = null)
    {
        var item = new FilingRule
        {
            Id = id,
            CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow
        };

        item.Update(category, description, isEnabled);
        return item;
    }

    public void Update(string category, string description, bool isEnabled)
    {
        Category = string.IsNullOrWhiteSpace(category) ? throw new ArgumentException("Category is required.", nameof(category)) : category.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? throw new ArgumentException("Description is required.", nameof(description)) : description.Trim();
        IsEnabled = isEnabled;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
