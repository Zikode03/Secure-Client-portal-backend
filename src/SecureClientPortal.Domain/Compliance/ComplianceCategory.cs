namespace SecureClientPortal.Backend.Models;

public class ComplianceCategory
{
    public string Id { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string? Code { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; private set; } = DateTime.UtcNow;

    public static ComplianceCategory Create(
        string id,
        string name,
        string description,
        string? code,
        bool isActive = true,
        DateTime? createdAtUtc = null)
    {
        var item = new ComplianceCategory
        {
            Id = id,
            CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow
        };

        item.UpdateDetails(name, description, code, isActive);
        return item;
    }

    public void UpdateDetails(string name, string description, string? code, bool isActive)
    {
        Name = string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("Category name is required.", nameof(name)) : name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? throw new ArgumentException("Category description is required.", nameof(description)) : description.Trim();
        Code = string.IsNullOrWhiteSpace(code) ? null : code.Trim().ToUpperInvariant();
        IsActive = isActive;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
