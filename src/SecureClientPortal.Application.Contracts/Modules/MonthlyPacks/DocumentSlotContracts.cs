namespace SecureClientPortal.Backend.Application.Contracts.Modules.MonthlyPacks;

public record CreateDocumentSlotRequest(
    Guid MonthlyPackId,
    string Category,
    string Label,
    bool IsRequired,
    DateTime? DueDateUtc);

public record DocumentSlotResponse(
    Guid Id,
    Guid MonthlyPackId,
    Guid ClientId,
    string Category,
    string Label,
    bool IsRequired,
    string Status,
    bool CanSubmit,
    Guid? CurrentDocumentId,
    DateTime? DueDateUtc,
    DateTime? SubmittedAtUtc,
    Guid? SubmittedByUserId,
    string ReviewStatus,
    string? RejectionReason,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
