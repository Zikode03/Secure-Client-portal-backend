using Microsoft.AspNetCore.Http;
using SecureClientPortal.Backend.Auth;
using System.Security.Claims;

namespace SecureClientPortal.Backend.Application;

public sealed class CurrentUserContextFactory : ICurrentUserContextFactory
{
    public CurrentUserContext Create(ClaimsPrincipal user, HttpContext? httpContext = null)
    {
        var explicitPermissions = user.FindAll("permission")
            .Select(x => x.Value)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var roleScope = user.GetRoleScope()
            ?? user.FindFirst(ClaimTypes.Role)?.Value?.Trim().ToLowerInvariant()
            ?? "client";
        var role = user.FindFirst(ClaimTypes.Role)?.Value?.Trim().ToLowerInvariant() ?? roleScope;

        var effectivePermissions = explicitPermissions.Length > 0
            ? explicitPermissions
            : RolePermissions.ForRole(roleScope).ToArray();

        return new CurrentUserContext(
            user.GetUserId(),
            role,
            roleScope,
            effectivePermissions,
            httpContext?.Connection.RemoteIpAddress?.ToString(),
            httpContext?.Request.Headers.UserAgent.ToString());
    }
}
