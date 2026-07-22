namespace SecureClientPortal.Backend.Application.Contracts.Modules.MonthlyPacks;

public record CreateMonthlyPackRequest(Guid ClientId, int Year, int Month, string? Status);
public record MonthlyPackResponse(
    Guid Id,
    Guid ClientId,
    int Year,
    int Month,
    string Status,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
