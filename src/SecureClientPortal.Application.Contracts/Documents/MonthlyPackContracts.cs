namespace SecureClientPortal.Backend.Application.Contracts;

public record CreateMonthlyPackRequest(Guid ClientId, int Year, int Month, string? Status);
