using SecureClientPortal.Backend.Application.Contracts;

namespace SecureClientPortal.Backend.Application.FirmManagement;

public interface IFirmManagementService
{
    Task<IReadOnlyList<RequiredDocumentTemplateDto>> GetRequiredDocumentTemplatesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<MonthlyPackTemplateDto>> GetMonthlyPackTemplatesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<RequestTemplateDto>> GetRequestTemplatesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ReminderRuleDto>> GetReminderRulesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<DeadlineRuleDto>> GetDeadlineRulesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<EscalationRuleDto>> GetEscalationRulesAsync(CancellationToken ct = default);
    Task ReplaceRequiredDocumentTemplatesAsync(IEnumerable<RequiredDocumentTemplateDto> templates, CurrentUserContext actor, CancellationToken ct = default);
    Task ReplaceMonthlyPackTemplatesAsync(IEnumerable<MonthlyPackTemplateDto> templates, CurrentUserContext actor, CancellationToken ct = default);
    Task ReplaceRequestTemplatesAsync(IEnumerable<RequestTemplateDto> templates, CurrentUserContext actor, CancellationToken ct = default);
    Task ReplaceReminderRulesAsync(IEnumerable<ReminderRuleDto> rules, CurrentUserContext actor, CancellationToken ct = default);
    Task ReplaceDeadlineRulesAsync(IEnumerable<DeadlineRuleDto> rules, CurrentUserContext actor, CancellationToken ct = default);
    Task ReplaceEscalationRulesAsync(IEnumerable<EscalationRuleDto> rules, CurrentUserContext actor, CancellationToken ct = default);
    Task SeedDefaultsAsync(CurrentUserContext actor, CancellationToken ct = default);
}
