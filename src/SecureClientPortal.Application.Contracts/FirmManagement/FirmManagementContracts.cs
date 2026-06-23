namespace SecureClientPortal.Backend.Application.Contracts;

public record RequiredDocumentTemplateDto(
    Guid Id,
    string Name,
    string Description,
    string DocumentCategory,
    bool IsRequired,
    int? DefaultDueDayOfMonth);

public record MonthlyPackTemplateDto(
    Guid Id,
    string Name,
    string Description,
    Guid[] RequiredDocumentTemplateIds,
    int AutoCreateDayOfMonth);

public record RequestTemplateDto(
    Guid Id,
    string Name,
    string RequestType,
    string TitleTemplate,
    string DescriptionTemplate,
    string Priority,
    int? DefaultDueInDays);

public record ReminderRuleDto(
    Guid Id,
    string Name,
    string TriggerType,
    int DaysBeforeDue,
    string AudienceRole,
    string MessageTemplate,
    bool IsEnabled);

public record DeadlineRuleDto(
    Guid Id,
    string Name,
    string Scope,
    int DueDayOfMonth,
    int GraceDays,
    string Priority,
    bool IsEnabled);

public record EscalationRuleDto(
    Guid Id,
    string Name,
    string TriggerType,
    int DaysAfterDue,
    string EscalateToRole,
    string Action,
    bool IsEnabled);
