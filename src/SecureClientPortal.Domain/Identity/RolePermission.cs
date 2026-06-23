namespace SecureClientPortal.Backend.Models;

public class RolePermission
{
    public Guid Id { get; private set; }
    public string RoleName { get; private set; } = string.Empty;
    public string PermissionKey { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;

    public static RolePermission Create(
        Guid id,
        string roleName,
        string permissionKey,
        DateTime? createdAtUtc = null)
    {
        return new RolePermission
        {
            Id = id,
            RoleName = string.IsNullOrWhiteSpace(roleName) ? throw new ArgumentException("Role name is required.", nameof(roleName)) : roleName.Trim().ToLowerInvariant(),
            PermissionKey = string.IsNullOrWhiteSpace(permissionKey) ? throw new ArgumentException("Permission key is required.", nameof(permissionKey)) : permissionKey.Trim(),
            CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow
        };
    }
}






