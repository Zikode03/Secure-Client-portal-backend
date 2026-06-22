namespace SecureClientPortal.Backend.Application.Contracts;

public record CreateDocumentSlotRequest(
    string MonthlyPackId,
    string Category,
    string Label,
    bool IsRequired,
    DateTime? DueDateUtc);
