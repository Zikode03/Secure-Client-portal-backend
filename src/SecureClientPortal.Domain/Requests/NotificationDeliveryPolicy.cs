namespace SecureClientPortal.Backend.Models;

public static class NotificationDeliveryPolicy
{
    public static bool Allows(NotificationPreference? preferences, string type, DateTime utcNow)
    {
        if (preferences is null) return true;
        var name = type.ToLowerInvariant();
        // Account/security events are always delivered.
        if (name.Contains("security") || name.StartsWith("auth.")) return true;
        var reminder = name.Contains("deadline") || name.Contains("reminder");
        var escalation = name.Contains("escalat") || name.Contains("overdue");
        if (reminder && !preferences.DeadlineAlerts || escalation && !preferences.EscalationAlerts ||
            name.Contains("compliance") && !preferences.ComplianceAlerts || name.Contains("reject") && !preferences.RejectionAlerts) return false;
        if (!reminder && !escalation) return true;
        var hours = preferences.QuietHours.Split('-');
        if (hours.Length != 2 || !TimeOnly.TryParseExact(hours[0], "HH:mm", out var start) || !TimeOnly.TryParseExact(hours[1], "HH:mm", out var end) || start == end) return true;
        // All portal reminder schedules use South Africa time (UTC+02:00).
        var now = TimeOnly.FromDateTime(utcNow.ToUniversalTime().AddHours(2));
        var quiet = start < end ? now >= start && now < end : now >= start || now < end;
        return !quiet;
    }
}
