using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace SecureClientPortal.Backend.Auth;

public static class UserScopeExtensions
{
    public static bool HasPermission(this ClaimsPrincipal user, string permission)
    {
        return user.FindAll("permission")
            .Any(x => string.Equals(x.Value, permission, StringComparison.OrdinalIgnoreCase));
    }

    public static string? GetUserId(this ClaimsPrincipal user)
    {
        return user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    }

    public static string? GetRoleScope(this ClaimsPrincipal user)
    {
        return user.FindFirst("role_scope")?.Value?.Trim().ToLowerInvariant();
    }

    public static bool IsAdmin(this ClaimsPrincipal user) => user.HasPermission("access.admin");
    public static bool IsAccountant(this ClaimsPrincipal user) => user.HasPermission("access.accountant");
    public static bool IsClient(this ClaimsPrincipal user) => user.HasPermission("access.client");

    public static async Task<HashSet<string>> GetAccessibleClientIdsAsync(this ClaimsPrincipal user, PortalDbContext db, CancellationToken ct = default)
    {
        if (user.IsAdmin())
        {
            var ids = await db.Clients.Select(x => x.Id).ToListAsync(ct);
            return ids.ToHashSet();
        }

        var userId = user.GetUserId();
        if (string.IsNullOrWhiteSpace(userId))
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
            var claimClientIds = user.FindAll("client_id").Select(x => x.Value).Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet();
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
                return System.Text.Json.JsonSerializer.Deserialize<string[]>(userRow.ClientIdsJson)?
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToHashSet() ?? [];
            }
            catch
            {
                return [];
            }
        }

        return [];
    }
}
