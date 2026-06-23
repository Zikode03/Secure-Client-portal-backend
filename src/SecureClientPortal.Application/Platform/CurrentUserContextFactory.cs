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

        var roleScope = user.FindFirst("role_scope")?.Value?.Trim().ToLowerInvariant()
            ?? user.FindFirst(ClaimTypes.Role)?.Value?.Trim().ToLowerInvariant()
            ?? "client";
        var role = user.FindFirst(ClaimTypes.Role)?.Value?.Trim().ToLowerInvariant() ?? roleScope;

        var effectivePermissions = explicitPermissions.Length > 0
            ? explicitPermissions
            : RolePermissions.ForRole(roleScope).ToArray();

        var userIdValue = user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        Guid? userId = Guid.TryParse(userIdValue, out var parsedUserId) ? parsedUserId : null;

        return new CurrentUserContext(
            userId,
            role,
            roleScope,
            effectivePermissions,
            httpContext?.Connection.RemoteIpAddress?.ToString(),
            httpContext?.Request.Headers.UserAgent.ToString());
    }
}
