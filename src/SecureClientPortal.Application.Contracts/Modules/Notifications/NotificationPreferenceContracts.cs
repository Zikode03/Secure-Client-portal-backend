namespace SecureClientPortal.Backend.Application.Contracts.Modules.Notifications;

public record NotificationPreferenceResponse(
    bool DeadlineAlerts,
    bool RejectionAlerts,
    bool ComplianceAlerts,
    bool WeeklySummary,
    bool BrowserAlerts,
    bool EmailReminders,
    bool EscalationAlerts,
    string QuietHours,
    DateTime? UpdatedAtUtc);

public record UpdateNotificationPreferenceRequest(
    bool DeadlineAlerts,
    bool RejectionAlerts,
    bool ComplianceAlerts,
    bool WeeklySummary,
    bool BrowserAlerts,
    bool EmailReminders,
    bool EscalationAlerts,
    string QuietHours);
