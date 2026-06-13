namespace SecureClientPortal.Backend.Application;

public sealed record CurrentUserContext(
    string? UserId,
    string Role,
    string RoleScope,
    IReadOnlyList<string> Permissions,
    string? IpAddress,
    string? UserAgent)
{
    public bool IsAdmin => string.Equals(RoleScope, "admin", StringComparison.OrdinalIgnoreCase);
    public bool IsAccountant => string.Equals(RoleScope, "accountant", StringComparison.OrdinalIgnoreCase);
    public bool IsClient => string.Equals(RoleScope, "client", StringComparison.OrdinalIgnoreCase);
}
