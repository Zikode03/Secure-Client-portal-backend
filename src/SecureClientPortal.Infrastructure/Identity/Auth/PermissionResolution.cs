using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Models;

namespace SecureClientPortal.Backend.Auth;

public static class PermissionResolution
{
    public static async Task<IReadOnlyList<string>> ResolvePermissionsAsync(
        PortalDbContext db,
        RoleDefinition? role,
        string fallbackRole,
        CancellationToken ct = default)
    {
        var roleName = role?.Name ?? fallbackRole;
        var scope = RolePermissions.NormalizeScope(role?.Scope ?? RolePermissions.ScopeForRole(roleName));
        var configuredPermissions = role is null
            ? RolePermissions.ForRole(roleName)
            : RolePermissions.ParsePermissions(role.PermissionsJson, role.Name);

        if (scope != "admin")
        {
            return configuredPermissions;
        }

        var activePermissions = await db.Permissions
            .Where(x => x.IsActive)
            .Select(x => x.Key)
            .ToListAsync(ct);

        return RolePermissions.NormalizePermissions(activePermissions.Concat(configuredPermissions));
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
