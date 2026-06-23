namespace SecureClientPortal.Backend.Models;

public enum UserRole { Admin, Accountant, Client }
public enum SecurityStatus { Active, Disabled, Locked, Invited, ResetPending, PasswordResetRequired }

public static class IdentityDomainValues
{
    public static string ToStorageValue(this UserRole role) => role switch
    {
        UserRole.Admin => "admin",
        UserRole.Accountant => "accountant",
        _ => "client"
    };

    public static UserRole ToUserRole(string raw) => (raw?.Trim().ToLowerInvariant()) switch
    {
        "admin" => UserRole.Admin,
        "accountant" => UserRole.Accountant,
        _ => UserRole.Client
    };

    public static string ToStorageValue(this SecurityStatus status) => status switch
    {
        SecurityStatus.Disabled => "disabled",
        SecurityStatus.Locked => "locked",
        SecurityStatus.Invited => "invited",
        SecurityStatus.ResetPending => "reset_pending",
        SecurityStatus.PasswordResetRequired => "password_reset_required",
        _ => "active"
    };

    public static SecurityStatus ToSecurityStatus(string raw) => (raw?.Trim().ToLowerInvariant()) switch
    {
        "disabled" => SecurityStatus.Disabled,
        "locked" => SecurityStatus.Locked,
        "invited" => SecurityStatus.Invited,
        "reset_pending" => SecurityStatus.ResetPending,
        "password_reset_required" => SecurityStatus.PasswordResetRequired,
        _ => SecurityStatus.Active
    };
}
