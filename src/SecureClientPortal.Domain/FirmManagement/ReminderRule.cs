namespace SecureClientPortal.Backend.Models;

public class ReminderRule
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string TriggerType { get; private set; } = string.Empty;
    public int DaysBeforeDue { get; private set; }
    public string AudienceRole { get; private set; } = "client";
    public string MessageTemplate { get; private set; } = string.Empty;
    public bool IsEnabled { get; private set; } = true;
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; private set; } = DateTime.UtcNow;

    public static ReminderRule Create(
        Guid id,
        string name,
        string triggerType,
        int daysBeforeDue,
        string audienceRole,
        string messageTemplate,
        bool isEnabled,
        DateTime? createdAtUtc = null)
    {
        var item = new ReminderRule
        {
            Id = id,
            CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow
        };

        item.Update(name, triggerType, daysBeforeDue, audienceRole, messageTemplate, isEnabled);
        return item;
    }

    public void Update(
        string name,
        string triggerType,
        int daysBeforeDue,
        string audienceRole,
        string messageTemplate,
        bool isEnabled)
    {
        if (daysBeforeDue < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(daysBeforeDue));
        }

        Name = string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("Rule name is required.", nameof(name)) : name.Trim();
        TriggerType = string.IsNullOrWhiteSpace(triggerType) ? throw new ArgumentException("Trigger type is required.", nameof(triggerType)) : triggerType.Trim();
        AudienceRole = string.IsNullOrWhiteSpace(audienceRole) ? throw new ArgumentException("Audience role is required.", nameof(audienceRole)) : audienceRole.Trim().ToLowerInvariant();
        MessageTemplate = string.IsNullOrWhiteSpace(messageTemplate) ? throw new ArgumentException("Message template is required.", nameof(messageTemplate)) : messageTemplate.Trim();
        DaysBeforeDue = daysBeforeDue;
        IsEnabled = isEnabled;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}






