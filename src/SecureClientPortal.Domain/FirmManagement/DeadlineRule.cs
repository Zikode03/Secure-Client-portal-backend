namespace SecureClientPortal.Backend.Models;

public class DeadlineRule
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Scope { get; private set; } = string.Empty;
    public int DueDayOfMonth { get; private set; }
    public int GraceDays { get; private set; }
    public string Priority { get; private set; } = "medium";
    public bool IsEnabled { get; private set; } = true;
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; private set; } = DateTime.UtcNow;

    public static DeadlineRule Create(
        Guid id,
        string name,
        string scope,
        int dueDayOfMonth,
        int graceDays,
        string priority,
        bool isEnabled,
        DateTime? createdAtUtc = null)
    {
        var item = new DeadlineRule
        {
            Id = id,
            CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow
        };

        item.Update(name, scope, dueDayOfMonth, graceDays, priority, isEnabled);
        return item;
    }

    public void Update(
        string name,
        string scope,
        int dueDayOfMonth,
        int graceDays,
        string priority,
        bool isEnabled)
    {
        if (dueDayOfMonth < 1 || dueDayOfMonth > 31)
        {
            throw new ArgumentOutOfRangeException(nameof(dueDayOfMonth));
        }

        if (graceDays < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(graceDays));
        }

        Name = string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("Rule name is required.", nameof(name)) : name.Trim();
        Scope = string.IsNullOrWhiteSpace(scope) ? throw new ArgumentException("Scope is required.", nameof(scope)) : scope.Trim().ToLowerInvariant();
        Priority = string.IsNullOrWhiteSpace(priority) ? throw new ArgumentException("Priority is required.", nameof(priority)) : priority.Trim().ToLowerInvariant();
        DueDayOfMonth = dueDayOfMonth;
        GraceDays = graceDays;
        IsEnabled = isEnabled;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}






