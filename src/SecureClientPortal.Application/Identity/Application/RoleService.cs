using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Application.Contracts;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Models;
using System.Text.Json;

namespace SecureClientPortal.Backend.Application.Roles;

public sealed class RoleService : IRoleService
{
    private readonly PortalDbContext _db;

    public RoleService(PortalDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<object>> GetAllAsync(CancellationToken ct = default)
    {
        var roles = await _db.RoleDefinitions
            .OrderBy(x => x.Scope)
            .ThenBy(x => x.DisplayName)
            .ToListAsync(ct);
        var roleNames = roles.Select(x => x.Name).ToArray();
        var rolePermissionMap = await _db.RolePermissions
            .Where(x => roleNames.Contains(x.RoleName))
            .GroupBy(x => x.RoleName)
            .ToDictionaryAsync(
                x => x.Key,
                x => (IReadOnlyList<string>)x
                    .Select(item => item.PermissionKey)
                    .OrderBy(item => item)
                    .ToArray(),
                ct);

        return roles.Select(role => (object)new
        {
            role.Name,
            role.DisplayName,
            role.Scope,
            permissions = rolePermissionMap.TryGetValue(role.Name, out var permissions) && permissions.Count > 0
                ? permissions
                : RolePermissions.ParsePermissions(role.PermissionsJson, role.Name),
            role.IsSystemRole,
            role.IsActive,
            role.CreatedAtUtc,
            role.UpdatedAtUtc
        }).ToArray();
    }

    public async Task<object> CreateAsync(CreateRoleRequest request, CurrentUserContext actor, CancellationToken ct = default)
    {
        var roleName = NormalizeRoleName(request.Name);
        if (string.IsNullOrWhiteSpace(roleName))
        {
            throw new InvalidOperationException("Role name is required.");
        }

        if (await _db.RoleDefinitions.AnyAsync(x => x.Name == roleName, ct))
        {
            throw new ArgumentException("A role with this name already exists.");
        }

        var normalizedScope = ValidateScope(request.Scope);
        var normalizedPermissions = RolePermissions.NormalizePermissions(
            request.Permissions?.Length > 0 ? request.Permissions : RolePermissions.ForRole(normalizedScope));

        var role = new RoleDefinition
        {
            Name = roleName,
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? roleName : request.DisplayName.Trim(),
            Scope = normalizedScope,
            PermissionsJson = RolePermissions.SerializePermissions(normalizedPermissions),
            IsSystemRole = false,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _db.RoleDefinitions.Add(role);
        await UpsertPermissionsAsync(normalizedPermissions, false, ct);
        await ReplaceRolePermissionsAsync(role.Name, normalizedPermissions, ct);
        await _db.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(
            actor.UserId,
            actor.RoleScope,
            "roles.created",
            "role",
            role.Name,
            null,
            JsonSerializer.Serialize(new { role.Name, role.Scope }),
            ct);

        return new
        {
            role.Name,
            role.DisplayName,
            role.Scope,
            permissions = normalizedPermissions,
            role.IsActive
        };
    }

    public async Task<object?> UpdateAsync(string name, UpdateRoleRequest request, CurrentUserContext actor, CancellationToken ct = default)
    {
        var role = await _db.RoleDefinitions.FirstOrDefaultAsync(x => x.Name == name, ct);
        if (role is null)
        {
            return null;
        }

        var normalizedScope = ValidateScope(request.Scope);
        var normalizedPermissions = RolePermissions.NormalizePermissions(
            request.Permissions?.Length > 0 ? request.Permissions : RolePermissions.ForRole(normalizedScope));

        role.DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? role.DisplayName : request.DisplayName.Trim();
        role.Scope = normalizedScope;
        role.PermissionsJson = RolePermissions.SerializePermissions(normalizedPermissions);
        role.UpdatedAtUtc = DateTime.UtcNow;
        await UpsertPermissionsAsync(normalizedPermissions, false, ct);
        await ReplaceRolePermissionsAsync(role.Name, normalizedPermissions, ct);
        await _db.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(
            actor.UserId,
            actor.RoleScope,
            "roles.updated",
            "role",
            role.Name,
            null,
            JsonSerializer.Serialize(new { role.Name, role.Scope }),
            ct);

        return new
        {
            role.Name,
            role.DisplayName,
            role.Scope,
            permissions = normalizedPermissions,
            role.IsActive
        };
    }

    public async Task<object?> UpdateActivationAsync(string name, bool isActive, CurrentUserContext actor, CancellationToken ct = default)
    {
        var role = await _db.RoleDefinitions.FirstOrDefaultAsync(x => x.Name == name, ct);
        if (role is null)
        {
            return null;
        }

        role.IsActive = isActive;
        role.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(
            actor.UserId,
            actor.RoleScope,
            isActive ? "roles.activated" : "roles.deactivated",
            "role",
            role.Name,
            null,
            JsonSerializer.Serialize(new { role.Name, isActive }),
            ct);

        return new { role.Name, role.IsActive };
    }

    private static string ValidateScope(string scope)
    {
        var normalizedScope = RolePermissions.NormalizeScope(scope);
        if (normalizedScope is not ("admin" or "accountant" or "client"))
        {
            throw new ArgumentException("Scope must be admin, accountant, or client.");
        }

        return normalizedScope;
    }

    private async Task UpsertPermissionsAsync(IEnumerable<string> permissions, bool isSystemPermission, CancellationToken ct)
    {
        foreach (var permissionKey in permissions)
        {
            var existing = await _db.Permissions.FirstOrDefaultAsync(x => x.Key == permissionKey, ct);
            if (existing is null)
            {
                _db.Permissions.Add(new Permission
                {
                    Key = permissionKey,
                    Name = permissionKey,
                    Description = $"Permission {permissionKey}",
                    IsSystemPermission = isSystemPermission,
                    IsActive = true,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                });
                continue;
            }

            existing.Name = permissionKey;
            existing.IsActive = true;
            existing.UpdatedAtUtc = DateTime.UtcNow;
        }
    }

    private async Task ReplaceRolePermissionsAsync(string roleName, IReadOnlyCollection<string> permissions, CancellationToken ct)
    {
        var existing = await _db.RolePermissions.Where(x => x.RoleName == roleName).ToListAsync(ct);
        if (existing.Count > 0)
        {
            _db.RolePermissions.RemoveRange(existing);
        }

        foreach (var permissionKey in permissions)
        {
            _db.RolePermissions.Add(new RolePermission
            {
                Id = $"rp_{Guid.NewGuid():N}",
                RoleName = roleName,
                PermissionKey = permissionKey,
                CreatedAtUtc = DateTime.UtcNow
            });
        }
    }

    private static string NormalizeRoleName(string value)
    {
        return value.Trim().ToLowerInvariant().Replace(" ", "_");
    }
}
