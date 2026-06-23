namespace SecureClientPortal.Backend.Models;

public class RoleDefinition
{
    public string Name { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public string Scope { get; private set; } = "client";
    public string PermissionsJson { get; private set; } = "[]";
    public bool IsSystemRole { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; private set; } = DateTime.UtcNow;

    public static RoleDefinition Create(
        string name,
        string displayName,
        string scope,
        string permissionsJson,
        bool isSystemRole,
        bool isActive = true,
        DateTime? createdAtUtc = null)
    {
        var role = new RoleDefinition
        {
            Name = string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("Role name is required.", nameof(name)) : name.Trim().ToLowerInvariant(),
            CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow
        };

        role.UpdateDefinition(displayName, scope, permissionsJson, isSystemRole);
        role.SetActivation(isActive);
        return role;
    }

    public void UpdateDefinition(string displayName, string scope, string permissionsJson, bool isSystemRole)
    {
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? throw new ArgumentException("Display name is required.", nameof(displayName)) : displayName.Trim();
        Scope = string.IsNullOrWhiteSpace(scope) ? throw new ArgumentException("Scope is required.", nameof(scope)) : scope.Trim().ToLowerInvariant();
        PermissionsJson = string.IsNullOrWhiteSpace(permissionsJson) ? "[]" : permissionsJson.Trim();
        IsSystemRole = isSystemRole;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void SetActivation(bool isActive)
    {
        IsActive = isActive;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}



