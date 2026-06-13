namespace SecureClientPortal.Backend.Application.Contracts;

public record RequiredDocumentTemplateDto(
    string Id,
    string Name,
    string Description,
    string DocumentCategory,
    bool IsRequired,
    int? DefaultDueDayOfMonth);

public record MonthlyPackTemplateDto(
    string Id,
    string Name,
    string Description,
    string[] RequiredDocumentTemplateIds,
    int AutoCreateDayOfMonth);

public record RequestTemplateDto(
    string Id,
    string Name,
    string RequestType,
    string TitleTemplate,
    string DescriptionTemplate,
    string Priority,
    int? DefaultDueInDays);

public record ReminderRuleDto(
    string Id,
    string Name,
    string TriggerType,
    int DaysBeforeDue,
    string AudienceRole,
    string MessageTemplate,
    bool IsEnabled);

public record DeadlineRuleDto(
    string Id,
    string Name,
    string Scope,
    int DueDayOfMonth,
    int GraceDays,
    string Priority,
    bool IsEnabled);

public record EscalationRuleDto(
    string Id,
    string Name,
    string TriggerType,
    int DaysAfterDue,
    string EscalateToRole,
    string Action,
    bool IsEnabled);
