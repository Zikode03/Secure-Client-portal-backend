namespace SecureClientPortal.Backend.Application.Contracts.Modules.Auth;

public record LoginRequest(string Email, string Password);
public record CompleteInviteRequest(string Email, string Token, string FullName, string Password);
public record ForgotPasswordRequest(string Email);
public record RefreshTokenRequest(string RefreshToken);
public record ChangePasswordRequest(string CurrentPassword, string NextPassword);
