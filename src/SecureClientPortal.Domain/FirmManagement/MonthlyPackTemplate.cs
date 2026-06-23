namespace SecureClientPortal.Backend.Models;

public class MonthlyPackTemplate
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public int AutoCreateDayOfMonth { get; private set; } = 1;
    public bool IsActive { get; private set; } = true;
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; private set; } = DateTime.UtcNow;

    public static MonthlyPackTemplate Create(
        Guid id,
        string name,
        string description,
        int autoCreateDayOfMonth,
        bool isActive = true,
        DateTime? createdAtUtc = null)
    {
        var item = new MonthlyPackTemplate
        {
            Id = id,
            CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow
        };

        item.Update(name, description, autoCreateDayOfMonth, isActive);
        return item;
    }

    public void Update(string name, string description, int autoCreateDayOfMonth, bool isActive)
    {
        if (autoCreateDayOfMonth < 1 || autoCreateDayOfMonth > 28)
        {
            throw new ArgumentOutOfRangeException(nameof(autoCreateDayOfMonth));
        }

        Name = string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("Template name is required.", nameof(name)) : name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? throw new ArgumentException("Template description is required.", nameof(description)) : description.Trim();
        AutoCreateDayOfMonth = autoCreateDayOfMonth;
        IsActive = isActive;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}






