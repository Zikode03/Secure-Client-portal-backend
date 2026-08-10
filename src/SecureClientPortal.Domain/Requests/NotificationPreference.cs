using System.Globalization;

namespace SecureClientPortal.Backend.Models;

public class NotificationPreference
{
    public Guid UserId { get; private set; }
    public bool DeadlineAlerts { get; private set; } = true;
    public bool RejectionAlerts { get; private set; } = true;
    public bool ComplianceAlerts { get; private set; } = true;
    public bool WeeklySummary { get; private set; }
    public bool BrowserAlerts { get; private set; }
    public bool EmailReminders { get; private set; } = true;
    public bool EscalationAlerts { get; private set; } = true;
    public string QuietHours { get; private set; } = "22:00-06:00";
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; private set; } = DateTime.UtcNow;

    public static NotificationPreference Create(Guid userId, DateTime? createdAtUtc = null)
    {
        if (userId == Guid.Empty) throw new DomainRuleException("User id is required.");
        var created = createdAtUtc ?? DateTime.UtcNow;
        return new NotificationPreference
        {
            UserId = userId,
            CreatedAtUtc = created,
            UpdatedAtUtc = created
        };
    }

    public void Update(
        bool deadlineAlerts,
        bool rejectionAlerts,
        bool complianceAlerts,
        bool weeklySummary,
        bool browserAlerts,
        bool emailReminders,
        bool escalationAlerts,
        string quietHours)
    {
        var normalizedQuietHours = NormalizeQuietHours(quietHours);
        DeadlineAlerts = deadlineAlerts;
        RejectionAlerts = rejectionAlerts;
        ComplianceAlerts = complianceAlerts;
        WeeklySummary = weeklySummary;
        BrowserAlerts = browserAlerts;
        EmailReminders = emailReminders;
        EscalationAlerts = escalationAlerts;
        QuietHours = normalizedQuietHours;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    private static string NormalizeQuietHours(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "22:00-06:00" : value.Trim();
        var parts = normalized.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 ||
            !TimeOnly.TryParseExact(parts[0], "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) ||
            !TimeOnly.TryParseExact(parts[1], "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
        {
            throw new DomainRuleException("Quiet hours must use the HH:mm-HH:mm format.");
        }

        return $"{start:HH:mm}-{end:HH:mm}";
    }
}
