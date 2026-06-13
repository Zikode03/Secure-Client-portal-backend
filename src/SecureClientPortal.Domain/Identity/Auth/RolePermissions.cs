namespace SecureClientPortal.Backend.Auth;

public static class RolePermissions
{
    public sealed record DefaultRoleDefinition(string Name, string DisplayName, string Scope, string[] Permissions);

    private static readonly IReadOnlyDictionary<string, DefaultRoleDefinition> DefaultsByRole =
        new Dictionary<string, DefaultRoleDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["admin"] = new(
                "admin",
                "Admin",
                "admin",
                [
                    "access.admin",
                    "access.accountant",
                    "access.client",
                    "auth.login",
                    "auth.logout",
                    "auth.me",
                    "users.read",
                    "users.create",
                    "users.update",
                    "users.activate",
                    "users.deactivate",
                    "roles.read",
                    "roles.create",
                    "roles.update",
                    "roles.activate",
                    "roles.deactivate",
                    "clients.read",
                    "clients.create",
                    "clients.update",
                    "clients.status.update",
                    "assignments.read",
                    "assignments.create",
                    "assignments.delete",
                    "audit_logs.read"
                ]),
            ["accountant"] = new(
                "accountant",
                "Accountant",
                "accountant",
                [
                    "access.accountant",
                    "auth.login",
                    "auth.logout",
                    "auth.me",
                    "clients.read",
                    "clients.create",
                    "clients.update",
                    "assignments.read",
                    "audit_logs.read"
                ]),
            ["client"] = new(
                "client",
                "Client",
                "client",
                [
                    "access.client",
                    "auth.login",
                    "auth.logout",
                    "auth.me",
                    "clients.read",
                    "assignments.read"
                ])
        };

    public static IReadOnlyCollection<DefaultRoleDefinition> DefaultRoles => DefaultsByRole.Values.ToArray();

    public static IReadOnlyList<string> ForRole(string role)
    {
        if (DefaultsByRole.TryGetValue(role.Trim(), out var definition))
        {
            return definition.Permissions;
        }

        return [];
    }

    public static string ScopeForRole(string role)
    {
        if (DefaultsByRole.TryGetValue(role.Trim(), out var definition))
        {
            return definition.Scope;
        }

        return "client";
    }

    public static string DisplayNameForRole(string role)
    {
        if (DefaultsByRole.TryGetValue(role.Trim(), out var definition))
        {
            return definition.DisplayName;
        }

        return role.Trim();
    }

    public static string NormalizeScope(string scope)
    {
        var normalized = scope.Trim().ToLowerInvariant();
        return normalized switch
        {
            "admin" => "admin",
            "accountant" => "accountant",
            "client" => "client",
            _ => normalized
        };
    }

    public static IReadOnlyList<string> ParsePermissions(string? permissionsJson, string? fallbackRole = null)
    {
        if (!string.IsNullOrWhiteSpace(permissionsJson))
        {
            try
            {
                return NormalizePermissions(System.Text.Json.JsonSerializer.Deserialize<string[]>(permissionsJson) ?? []);
            }
            catch
            {
                // Fall back to defaults below.
            }
        }

        return string.IsNullOrWhiteSpace(fallbackRole) ? [] : ForRole(fallbackRole);
    }

    public static string SerializePermissions(IEnumerable<string> permissions)
    {
        return System.Text.Json.JsonSerializer.Serialize(NormalizePermissions(permissions));
    }

    public static IReadOnlyList<string> NormalizePermissions(IEnumerable<string> permissions)
    {
        return permissions
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
