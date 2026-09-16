namespace SecureClientPortal.Backend.Application.Contracts.Modules.MonthlyPacks;

// Describes one recurring requirement in a client's monthly-pack profile.
// Source tells the UI whether the item came from the firm template or was tailored for this client.
// DefaultDueDayOfMonth lets future packs recreate a recurring deadline without storing a fixed date.
public record ClientMonthlyPackProfileItemDto(
    Guid Id,
    string Category,
    string Label,
    bool IsRequired,
    string Source,
    int? DefaultDueDayOfMonth,
    string Cadence = "monthly",
    DateTime? EffectiveFromUtc = null,
    DateTime? EffectiveToUtc = null,
    string? Reason = null);

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
    Guid RequestedByUserId,
    int? DefaultDueDayOfMonth);

// Current pack items include source metadata that DocumentSlot itself intentionally does not store.
public record ClientMonthlyPackCurrentItemDto(
    Guid SlotId,
    string Category,
    string Label,
    bool IsRequired,
    string Status,
    string Source,
    DateTime? DueDateUtc,
    string? Reason = null);

// Nullable operating answers represent facts that have not yet been confirmed. Unknown answers
// never create compulsory upload slots; they are returned as recommendations for professional review.
public record ClientOperatingProfileDto(
    DateTime EffectiveFromUtc,
    bool? VatRegistered,
    int VatCycleMonths,
    int VatAnchorMonth,
    bool? HasEmployees,
    bool? HoldsInventory,
    bool? UsesSupplierAccounts,
    bool? UsesPos,
    bool? OperatesFleet,
    bool? UsesSubcontractors,
    bool? UsesPaymentCertificates,
    bool? TracksProjectCosts,
    bool? UsesBookingPlatforms,
    bool? UsesFoodSuppliers,
    bool? ManufacturesGoods,
    bool BankFeedConnected, // Legacy profile preference; BankConnections alone determine live Banking state.
    bool SalesInvoicesSynced,
    bool PurchaseInvoicesSynced,
    bool IsComplete);

public record ClientOperatingProfileInput(
    bool? VatRegistered = null,
    int VatCycleMonths = 2,
    int VatAnchorMonth = 1,
    bool? HasEmployees = null,
    bool? HoldsInventory = null,
    bool? UsesSupplierAccounts = null,
    bool? UsesPos = null,
    bool? OperatesFleet = null,
    bool? UsesSubcontractors = null,
    bool? UsesPaymentCertificates = null,
    bool? TracksProjectCosts = null,
    bool? UsesBookingPlatforms = null,
    bool? UsesFoodSuppliers = null,
    bool? ManufacturesGoods = null,
    bool BankFeedConnected = false,
    bool SalesInvoicesSynced = false,
    bool PurchaseInvoicesSynced = false);

public record MonthlyPackRequirementRecommendationDto(
    string Category,
    string Label,
    bool IsRequired,
    string Cadence,
    string Decision,
    string Reason);

public record MonthlyPackReconciliationResultDto(
    Guid MonthlyPackId,
    int Added,
    int Removed,
    int PreservedWithEvidence,
    int Updated);

public record ClientMonthlyPackProfileDto(
    Guid ClientId,
    Guid? TemplateId,
    string? TemplateName,
    IReadOnlyList<ClientMonthlyPackTemplateOptionDto> AvailableTemplates,
    IReadOnlyList<ClientMonthlyPackProfileItemDto> RecurringItems,
    IReadOnlyList<PendingRecurringPackItemDto> PendingRecurringItems,
    IReadOnlyList<ClientMonthlyPackCurrentItemDto> CurrentPackItems,
    DateTime UpdatedAtUtc,
    ClientOperatingProfileDto? OperatingProfile = null,
    IReadOnlyList<MonthlyPackRequirementRecommendationDto>? Recommendations = null,
    Guid? RecommendedTemplateId = null,
    string? RecommendedTemplateReason = null);

public record UpdateClientMonthlyPackProfileRequest(
    Guid? TemplateId,
    ClientMonthlyPackProfileItemInput[] RecurringItems,
    ClientOperatingProfileInput? OperatingProfile = null,
    DateTime? EffectiveFromUtc = null,
    bool ReconcileCurrentPack = false);

public record ClientMonthlyPackProfileItemInput(
    string Category,
    string Label,
    bool IsRequired,
    int? DefaultDueDayOfMonth = null,
    string Cadence = "monthly",
    DateTime? EffectiveFromUtc = null,
    DateTime? EffectiveToUtc = null);

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
