namespace SecureClientPortal.Backend.Models;

public class Permission
{
    public string Key { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public bool IsSystemPermission { get; private set; } = true;
    public bool IsActive { get; private set; } = true;
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; private set; } = DateTime.UtcNow;

    public static Permission Create(
        string key,
        string name,
        string? description,
        bool isSystemPermission,
        bool isActive = true,
        DateTime? createdAtUtc = null)
    {
        var permission = new Permission
        {
            Key = string.IsNullOrWhiteSpace(key) ? throw new ArgumentException("Permission key is required.", nameof(key)) : key.Trim(),
            CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow
        };

        permission.UpdateDetails(name, description, isSystemPermission, isActive);
        return permission;
    }

    public void UpdateDetails(string name, string? description, bool isSystemPermission, bool isActive)
    {
        Name = string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("Permission name is required.", nameof(name)) : name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        IsSystemPermission = isSystemPermission;
        IsActive = isActive;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
