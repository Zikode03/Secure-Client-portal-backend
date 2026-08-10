namespace SecureClientPortal.Backend.Application.Contracts.Modules.Auth;

public record LoginRequest(string Email, string Password);
public record CompleteInviteRequest(string Email, string Token, string FullName, string Password);
public record ForgotPasswordRequest(string Email);
public record RefreshTokenRequest(string RefreshToken);
public record ChangePasswordRequest(string CurrentPassword, string NextPassword);
public record UpdateSecuritySettingsRequest(string? RecoveryEmail);
public record SecuritySessionResponse(
    Guid Id,
    DateTime IssuedAtUtc,
    DateTime ExpiresAtUtc,
    string? ClientIp,
    string? Device,
    bool IsCurrent);
public record SecuritySettingsResponse(
    bool MfaSupported,
    bool MfaEnabled,
    DateTime? PasswordLastChangedAtUtc,
    string? RecoveryEmail,
    IReadOnlyCollection<SecuritySessionResponse> Sessions);
public record SessionRevocationResponse(int RevokedCount);
