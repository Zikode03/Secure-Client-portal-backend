namespace SecureClientPortal.Backend.Application.Contracts;

public record CreateUserRequest(
    string FullName,
    string Email,
    string Role,
    string? Password,
    Guid[]? ClientIds,
    string? Company);
public record UpdateUserActivationRequest(bool IsActive, string? Reason);

public record AdminCreateUserRequest(string FullName, string Email, string Role, string? Company);
public record AdminUpdateRoleRequest(string Role);
public record AdminUpdateStatusRequest(string Status);
public record AdminResetAccessRequest(string Reason);
public record AdminResetPasswordRequest(string? NewPassword, string? Reason);
public record AdminSettingRequest(string ValueJson);

public record LoginRequest(string Email, string Password);
public record CompleteInviteRequest(string Email, string Token, string FullName, string Password);
public record ForgotPasswordRequest(string Email);
public record RefreshTokenRequest(string RefreshToken);
public record ChangePasswordRequest(string CurrentPassword, string NextPassword);
