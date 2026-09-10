namespace SecureClientPortal.Backend.Api.Security;

public sealed class ApiSecurityHeadersMiddleware(RequestDelegate next, IHostEnvironment environment)
{
    public static void Apply(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";
        headers.CacheControl = "no-store";
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!(environment.IsDevelopment() && context.Request.Path.StartsWithSegments("/swagger"))) Apply(context);
        if (!environment.IsDevelopment() && !context.Request.IsHttps && !CsrfProtectionMiddleware.IsSafeMethod(context.Request.Method))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { code = "HTTPS_REQUIRED", message = "Use HTTPS for this request." }, context.RequestAborted);
            return;
        }
        await next(context);
    }
}
