namespace SecureClientPortal.Backend.Models;

public class RequiredDocumentTemplate
{
    public string Id { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string DocumentCategory { get; private set; } = string.Empty;
    public bool IsRequired { get; private set; } = true;
    public int? DefaultDueDayOfMonth { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; private set; } = DateTime.UtcNow;

    public static RequiredDocumentTemplate Create(
        string id,
        string name,
        string description,
        string documentCategory,
        bool isRequired,
        int? defaultDueDayOfMonth,
        bool isActive = true,
        DateTime? createdAtUtc = null)
    {
        var item = new RequiredDocumentTemplate
        {
            Id = id,
            CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow
        };

        item.Update(name, description, documentCategory, isRequired, defaultDueDayOfMonth, isActive);
        return item;
    }

    public void Update(
        string name,
        string description,
        string documentCategory,
        bool isRequired,
        int? defaultDueDayOfMonth,
        bool isActive)
    {
        if (defaultDueDayOfMonth is < 1 or > 31)
        {
            throw new ArgumentOutOfRangeException(nameof(defaultDueDayOfMonth));
        }

        Name = string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("Template name is required.", nameof(name)) : name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? throw new ArgumentException("Template description is required.", nameof(description)) : description.Trim();
        DocumentCategory = string.IsNullOrWhiteSpace(documentCategory) ? throw new ArgumentException("Document category is required.", nameof(documentCategory)) : documentCategory.Trim();
        IsRequired = isRequired;
        DefaultDueDayOfMonth = defaultDueDayOfMonth;
        IsActive = isActive;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
