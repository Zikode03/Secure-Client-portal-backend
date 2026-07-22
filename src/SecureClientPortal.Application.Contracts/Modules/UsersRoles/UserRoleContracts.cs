namespace SecureClientPortal.Backend.Application.Contracts.Modules.UsersRoles;

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

public record CreateRoleRequest(string Name, string DisplayName, string Scope, string[]? Permissions);
public record UpdateRoleRequest(string? DisplayName, string Scope, string[]? Permissions);
