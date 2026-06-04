namespace SecureClientPortal.Backend.Auth;

public static class RolePermissions
{
    private static readonly IReadOnlyDictionary<string, string[]> PermissionsByRole =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["admin"] =
            [
                "auth.login",
                "users.read",
                "users.create",
                "users.update_role",
                "clients.read",
                "clients.create",
                "clients.update",
                "assignments.read",
                "assignments.create",
                "assignments.delete",
                "audit_logs.read"
            ],
            ["accountant"] =
            [
                "auth.login",
                "clients.read",
                "clients.create",
                "clients.update",
                "assignments.read",
                "audit_logs.read"
            ],
            ["client"] =
            [
                "auth.login",
                "clients.read",
                "assignments.read"
            ]
        };

    public static IReadOnlyList<string> ForRole(string role)
    {
        if (PermissionsByRole.TryGetValue(role.Trim(), out var permissions))
        {
            return permissions;
        }

        return [];
    }
}
