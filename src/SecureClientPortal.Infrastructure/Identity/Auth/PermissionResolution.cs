using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Models;

namespace SecureClientPortal.Backend.Auth;

public static class PermissionResolution
{
    public static Task<IReadOnlyList<string>> ResolvePermissionsAsync(
        PortalDbContext db, RoleDefinition? role, string fallbackRole, CancellationToken ct = default)
    {
        IReadOnlyList<string> configured = role is null
            ? RolePermissions.ForRole(fallbackRole)
            : RolePermissions.ParsePermissions(role.PermissionsJson);
        return Task.FromResult(configured);
    }

    public static IReadOnlyList<string> FilterClientVisiblePermissions(IEnumerable<string> permissions)
    {
        return RolePermissions.NormalizePermissions(
            permissions
                .Where(permission => !permission.StartsWith("access.", StringComparison.OrdinalIgnoreCase))
                .Where(permission => !string.Equals(permission, "auth.logout", StringComparison.OrdinalIgnoreCase))
                .Where(permission => !string.Equals(permission, "auth.me", StringComparison.OrdinalIgnoreCase)));
    }
}
