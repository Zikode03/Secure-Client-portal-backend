namespace SecureClientPortal.Backend.Application.Contracts.Modules.MonthlyPacks;

// Describes one recurring requirement in a client's monthly-pack profile.
// Source tells the UI whether the item came from the firm template or was tailored for this client.
public record ClientMonthlyPackProfileItemDto(
    Guid Id,
    string Category,
    string Label,
    bool IsRequired,
    string Source);

// Lightweight template option used by Accountant/Admin when choosing the starting point for a client.
public record ClientMonthlyPackTemplateOptionDto(
    Guid Id,
    string Name,
    string Description);

// A recurring request created by the client must be reviewed by the accountant/admin before
// it becomes part of future monthly packs. The item is still available in the current month.
public record PendingRecurringPackItemDto(
    Guid Id,
    string Category,
    string Label,
    bool IsRequired,
    DateTime RequestedAtUtc,
    Guid RequestedByUserId);

// Current pack items include source metadata that DocumentSlot itself intentionally does not store.
public record ClientMonthlyPackCurrentItemDto(
    Guid SlotId,
    string Category,
    string Label,
    bool IsRequired,
    string Status,
    string Source,
    DateTime? DueDateUtc);

public record ClientMonthlyPackProfileDto(
    Guid ClientId,
    Guid? TemplateId,
    string? TemplateName,
    IReadOnlyList<ClientMonthlyPackTemplateOptionDto> AvailableTemplates,
    IReadOnlyList<ClientMonthlyPackProfileItemDto> RecurringItems,
    IReadOnlyList<PendingRecurringPackItemDto> PendingRecurringItems,
    IReadOnlyList<ClientMonthlyPackCurrentItemDto> CurrentPackItems,
    DateTime UpdatedAtUtc);

public record UpdateClientMonthlyPackProfileRequest(
    Guid? TemplateId,
    ClientMonthlyPackProfileItemInput[] RecurringItems);

public record ClientMonthlyPackProfileItemInput(
    string Category,
    string Label,
    bool IsRequired);

// Recurrence accepts "this_month" or "every_month".
// Clients may request recurring items, while accountants/admins can add recurring items immediately.
public record AddClientMonthlyPackItemRequest(
    string Category,
    string Label,
    bool IsRequired,
    string Recurrence,
    DateTime? DueDateUtc);

public record AddClientMonthlyPackItemResponse(
    Guid SlotId,
    Guid MonthlyPackId,
    Guid? RecurringRequestId,
    string Recurrence,
    string Source);
