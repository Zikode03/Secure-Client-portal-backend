using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application;
using SecureClientPortal.Backend.Application.Roles;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Application.Contracts;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SecureClientPortal.Backend.Infrastructure.Identity.Application;

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

        var payload = new List<object>(roles.Count);
        foreach (var role in roles)
        {
            var configuredPermissions = rolePermissionMap.TryGetValue(role.Name, out var permissions) && permissions.Count > 0
                ? permissions
                : RolePermissions.ParsePermissions(role.PermissionsJson, role.Name);
            var effectivePermissions = await PermissionResolution.ResolvePermissionsAsync(_db, role, role.Name, ct);

            payload.Add(new
            {
                role.Name,
                role.DisplayName,
                role.Scope,
                permissions = effectivePermissions,
                configuredPermissions,
                role.IsSystemRole,
                role.IsActive,
                role.CreatedAtUtc,
                role.UpdatedAtUtc
            });
        }

        return payload;
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

        var role = RoleDefinition.Create(
            roleName,
            string.IsNullOrWhiteSpace(request.DisplayName) ? roleName : request.DisplayName,
            normalizedScope,
            RolePermissions.SerializePermissions(normalizedPermissions),
            false,
            true);

        _db.RoleDefinitions.Add(role);
        await UpsertPermissionsAsync(normalizedPermissions, false, ct);
        await ReplaceRolePermissionsAsync(role.Name, normalizedPermissions, ct);
        await _db.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(
            actor.UserId,
            actor.RoleScope,
            "roles.created",
            "role",
            DeterministicGuid($"role:{role.Name}"),
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

        role.UpdateDefinition(
            string.IsNullOrWhiteSpace(request.DisplayName) ? role.DisplayName : request.DisplayName,
            normalizedScope,
            RolePermissions.SerializePermissions(normalizedPermissions),
            role.IsSystemRole);

        await UpsertPermissionsAsync(normalizedPermissions, false, ct);
        await ReplaceRolePermissionsAsync(role.Name, normalizedPermissions, ct);
        await _db.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(
            actor.UserId,
            actor.RoleScope,
            "roles.updated",
            "role",
            DeterministicGuid($"role:{role.Name}"),
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

        role.SetActivation(isActive);
        await _db.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(
            actor.UserId,
            actor.RoleScope,
            isActive ? "roles.activated" : "roles.deactivated",
            "role",
            DeterministicGuid($"role:{role.Name}"),
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
                _db.Permissions.Add(Permission.Create(
                    permissionKey,
                    permissionKey,
                    $"Permission {permissionKey}",
                    isSystemPermission,
                    true));
                continue;
            }

            existing.UpdateDetails(
                permissionKey,
                existing.Description,
                existing.IsSystemPermission || isSystemPermission,
                true);
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
            _db.RolePermissions.Add(RolePermission.Create(
                Guid.NewGuid(),
                roleName,
                permissionKey));
        }
    }

    private static Guid DeterministicGuid(string value)
    {
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(value));
        return new Guid(hash);
    }

    private static string NormalizeRoleName(string value)
    {
        return value.Trim().ToLowerInvariant().Replace(" ", "_");
    }
}

