namespace SecureClientPortal.Backend.Application.Contracts.Modules.Reports;

public record CreateReportScheduleRequest(
    Guid? ClientId,
    string Frequency,
    IReadOnlyCollection<string> Recipients);
public record UpdateReportScheduleRequest(
    string Frequency,
    IReadOnlyCollection<string> Recipients);
public record ReportScheduleResponse(
    Guid Id,
    Guid CreatedByUserId,
    Guid? ClientId,
    string ReportType,
    string Frequency,
    IReadOnlyCollection<string> Recipients,
    DateTime NextRunAtUtc,
    DateTime LastScheduledAtUtc,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
public record ReportFileResponse(byte[] Content, string ContentType, string FileName);
