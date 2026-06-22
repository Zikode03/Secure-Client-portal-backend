namespace SecureClientPortal.Backend.Application.Contracts;

public record CreateMonthlyPackRequest(string ClientId, int Year, int Month, string? Status);
public record UpdateMonthlyPackStatusRequest(string Status);
