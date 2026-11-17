using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SecureClientPortal.Backend.Application;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;

namespace SecureClientPortal.Backend.Infrastructure.Modules.UsersRoles.Application;

// Ownership is an installation setting, never a claim or an editable firm setting.
internal static class AdministrationAccess
{
    internal static readonly string[] RequiredAdminPermissions = [
        "access.admin", "auth.login", "auth.me", "users.read", "users.create", "users.update",
        "users.activate", "users.deactivate", "roles.read", "roles.create", "roles.update", "roles.activate", "roles.deactivate"
    ];

    internal static bool IsOwner(IConfiguration? configuration, Guid id) =>
        Guid.TryParse(configuration?["Administration:OwnerUserId"], out var ownerId) && ownerId != Guid.Empty && id == ownerId;

    internal static async Task RequireOwnerAsync(PortalDbContext db, IConfiguration? configuration, CurrentUserContext actor, string permission, CancellationToken ct)
    {
        if (actor.UserId is not Guid id || !IsOwner(configuration, id))
            throw new AppForbiddenException("Only the designated system owner can change role permissions. Ask the installation owner to configure this access.");
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == id, ct);
        var role = user is null ? null : await db.RoleDefinitions.SingleOrDefaultAsync(x => x.Name == user.Role, ct);
        if (user is null || UserSecurityProfile.GetStatus(user.SecurityJson) != "active" || role is null || !role.IsActive || role.Scope != "admin")
            throw new AppForbiddenException("The system owner must have an active administrator account.");
        var permissions = await PermissionResolution.ResolvePermissionsAsync(db, role, user.Role, ct);
        if (!permissions.Contains("access.admin") || !permissions.Contains(permission))
            throw new AppForbiddenException("The system owner does not have permission for this operation.");
    }

    internal static async Task<bool> HasOtherActiveAdminAsync(PortalDbContext db, Guid except, CancellationToken ct)
    {
        var role = await db.RoleDefinitions.SingleOrDefaultAsync(x => x.Name == "admin", ct);
        if (role is null || !role.IsActive || role.Scope != "admin") return false;
        var permissions = await PermissionResolution.ResolvePermissionsAsync(db, role, "admin", ct);
        if (RequiredAdminPermissions.Any(permission => !permissions.Contains(permission))) return false;
        var candidates = await db.Users.Where(x => x.Role == "admin" && x.Id != except).Select(x => x.SecurityJson).ToListAsync(ct);
        return candidates.Any(json => UserSecurityProfile.GetStatus(json) == "active");
    }
}
