namespace SecureClientPortal.Backend.Application.Contracts;

public record CreateDocumentSlotRequest(
    Guid MonthlyPackId,
    string Category,
    string Label,
    bool IsRequired,
    DateTime? DueDateUtc);
