using System.Security.Claims;
using SecureClientPortal.Backend.Models;

namespace SecureClientPortal.Backend.Auth;

public static class CurrentAuthorizationClaims
{
    public static void Replace(ClaimsPrincipal principal, User user, RoleDefinition role, IEnumerable<string> permissions)
    {
        // Remove authorization claims from every identity, not just the primary identity.
        foreach (var identity in principal.Identities)
            foreach (var claim in identity.Claims.Where(x => x.Type is "permission" or "role_scope" or "client_id" or "assigned_client_id" || x.Type == RolePermissions.AuthoritativeClaimType || x.Type == ClaimTypes.Role).ToList())
                identity.RemoveClaim(claim);
        var target = principal.Identities.First(x => x.IsAuthenticated);
        target.AddClaim(new Claim(ClaimTypes.Role, user.Role));
        target.AddClaim(new Claim("role_scope", role.Scope));
        target.AddClaim(new Claim(RolePermissions.AuthoritativeClaimType, "true"));
        foreach (var permission in RolePermissions.NormalizePermissions(permissions)) target.AddClaim(new Claim("permission", permission));
        // Membership is now read from current database assignments, never an old token.
    }
}
