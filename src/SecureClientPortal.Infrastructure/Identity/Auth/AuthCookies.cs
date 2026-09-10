using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace SecureClientPortal.Backend.Auth;

public static class AuthCookies
{
    public const string AccessTokenName = "scp_access";
    public const string RefreshTokenName = "scp_refresh";
    private const string RememberName = "scp_remember";

    public static string? ReadRefreshToken(HttpContext context) =>
        context.Request.Cookies.TryGetValue(RefreshTokenName, out var value) ? value : null;

    public static bool IsPersistent(HttpContext context) =>
        context.Request.Cookies.TryGetValue(RememberName, out var value) && value == "1";

    public static void Write(
        HttpContext context,
        string accessToken,
        DateTime accessExpiresAtUtc,
        string refreshToken,
        DateTime refreshExpiresAtUtc,
        bool persistent)
    {
        context.Response.Cookies.Append(
            AccessTokenName,
            accessToken,
            Options(context, accessExpiresAtUtc));
        context.Response.Cookies.Append(
            RefreshTokenName,
            refreshToken,
            Options(context, persistent ? refreshExpiresAtUtc : null));
        context.Response.Cookies.Append(
            RememberName,
            persistent ? "1" : "0",
            Options(context, persistent ? refreshExpiresAtUtc : null));
    }

    public static void Clear(HttpContext context)
    {
        var options = Options(context, DateTime.UnixEpoch);
        context.Response.Cookies.Delete(AccessTokenName, options);
        context.Response.Cookies.Delete(RefreshTokenName, options);
        context.Response.Cookies.Delete(RememberName, options);
    }

    private static CookieOptions Options(HttpContext context, DateTime? expiresAtUtc)
    {
        var environment = context.RequestServices.GetService<IHostEnvironment>();
        return new CookieOptions
        {
            HttpOnly = true,
            Secure = context.Request.IsHttps || environment is { } && !environment.IsDevelopment(),
            SameSite = SameSiteMode.Strict,
            Path = "/",
            IsEssential = true,
            Expires = expiresAtUtc.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(expiresAtUtc.Value, DateTimeKind.Utc)) : null
        };
    }
}
