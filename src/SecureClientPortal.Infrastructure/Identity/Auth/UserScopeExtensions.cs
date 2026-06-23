using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;

namespace SecureClientPortal.Backend.Auth;

public static class UserScopeExtensions
{
    public static bool HasPermission(this ClaimsPrincipal user, string permission)
    {
        var explicitPermissions = user.FindAll("permission").Select(x => x.Value).ToArray();
        if (explicitPermissions.Any(x => string.Equals(x, permission, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (explicitPermissions.Any(x => string.Equals(x, "access.admin", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var effectiveRole = GetEffectiveRole(user);
        if (string.Equals(effectiveRole, "admin", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(effectiveRole) &&
            RolePermissions.ForRole(effectiveRole).Contains(permission, StringComparer.OrdinalIgnoreCase);
    }

    public static Guid? GetUserId(this ClaimsPrincipal user)
    {
        var rawValue = user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(rawValue, out var userId) ? userId : null;
    }

    public static string? GetRoleScope(this ClaimsPrincipal user)
    {
        return user.FindFirst("role_scope")?.Value?.Trim().ToLowerInvariant();
    }

    public static bool IsAdmin(this ClaimsPrincipal user) => user.HasPermission("access.admin");
    public static bool IsAccountant(this ClaimsPrincipal user) => user.HasPermission("access.accountant");
    public static bool IsClient(this ClaimsPrincipal user) => user.HasPermission("access.client");

    public static async Task<HashSet<Guid>> GetAccessibleClientIdsAsync(this ClaimsPrincipal user, PortalDbContext db, CancellationToken ct = default)
    {
        if (user.IsAdmin())
        {
            var ids = await db.Clients.Select(x => x.Id).ToListAsync(ct);
            return ids.ToHashSet();
        }

        var userId = user.GetUserId();
        if (userId is null || userId == Guid.Empty)
        {
            return [];
        }

        if (user.IsAccountant())
        {
            var ids = await db.ClientAssignments
                .Where(x => x.AccountantUserId == userId)
                .Select(x => x.ClientId)
                .ToListAsync(ct);
            return ids.ToHashSet();
        }

        if (user.IsClient())
        {
            var claimClientIds = user.FindAll("client_id")
                .Select(x => Guid.TryParse(x.Value, out var clientId) ? clientId : Guid.Empty)
                .Where(x => x != Guid.Empty)
                .ToHashSet();
            if (claimClientIds.Count > 0)
            {
                return claimClientIds;
            }

            var userRow = await db.Users.FirstOrDefaultAsync(x => x.Id == userId, ct);
            if (userRow is null || string.IsNullOrWhiteSpace(userRow.ClientIdsJson))
            {
                return [];
            }

            try
            {
                return (JsonSerializer.Deserialize<string[]>(userRow.ClientIdsJson) ?? [])
                    .Select(x => Guid.TryParse(x, out var clientId) ? clientId : Guid.Empty)
                    .Where(x => x != Guid.Empty)
                    .ToHashSet();
            }
            catch
            {
                return [];
            }
        }

        return [];
    }

    private static string? GetEffectiveRole(ClaimsPrincipal user)
    {
        return user.GetRoleScope()
            ?? user.FindFirst(ClaimTypes.Role)?.Value?.Trim().ToLowerInvariant();
    }
}
