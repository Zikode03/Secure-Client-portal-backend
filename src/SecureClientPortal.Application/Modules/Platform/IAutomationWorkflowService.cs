namespace SecureClientPortal.Backend.Application.Modules.Platform;

public interface IAutomationWorkflowService
{
    Task<AutomationRunSummary> RunAsync(DateTime? utcNow = null, CancellationToken ct = default);
}

public sealed record AutomationRunSummary(
    DateTime RunAtUtc,
    int MonthlyPacksCreated,
    int DocumentSlotsCreated,
    int DraftSlotsAutoSubmitted,
    int MonthlyPackNotificationsSent,
    int MonthlyPackDeadlineNotificationsSent,
    int OverdueRequestsMarked,
    int RequestEscalationNotificationsSent,
    int ComplianceItemsUpdated,
    int ComplianceReminderNotificationsSent);
