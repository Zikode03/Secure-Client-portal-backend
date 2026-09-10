using Microsoft.AspNetCore.Antiforgery;

namespace SecureClientPortal.Backend.Api.Security;

public sealed class CsrfProtectionMiddleware
{
    public const string HeaderName = "X-CSRF-Token";
    private readonly RequestDelegate _next;
    private readonly HashSet<string> _origins;

    public CsrfProtectionMiddleware(RequestDelegate next, IEnumerable<string> origins)
    {
        _next = next;
        _origins = new HashSet<string>(origins, StringComparer.OrdinalIgnoreCase);
    }

    public static bool IsSafeMethod(string method) => HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);

    public async Task InvokeAsync(HttpContext context, IAntiforgery antiforgery)
    {
        if (!context.Request.Path.StartsWithSegments("/api") || IsSafeMethod(context.Request.Method))
        {
            await _next(context);
            return;
        }
        var origin = context.Request.Headers.Origin.ToString();
        var referer = context.Request.Headers.Referer.ToString();
        var sameOrigin = $"{context.Request.Scheme}://{context.Request.Host}";
        bool trusted(string value) => string.Equals(value, sameOrigin, StringComparison.OrdinalIgnoreCase) || _origins.Contains(value);
        if ((origin.Length > 0 && !trusted(origin)) ||
            (origin.Length == 0 && referer.Length > 0 && (!Uri.TryCreate(referer, UriKind.Absolute, out var uri) || !trusted(uri.GetLeftPart(UriPartial.Authority)))) ||
            (origin.Length == 0 && referer.Length == 0 && context.Request.Headers["Sec-Fetch-Site"] == "cross-site"))
        {
            await Reject(context, "ORIGIN_NOT_ALLOWED", "This request origin is not allowed.");
            return;
        }
        try
        {
            // Require the header explicitly; never accept a token supplied only as a form field.
            if (string.IsNullOrWhiteSpace(context.Request.Headers[HeaderName])) throw new AntiforgeryValidationException("Missing CSRF header.");
            await antiforgery.ValidateRequestAsync(context);
        }
        catch (AntiforgeryValidationException)
        {
            await Reject(context, "CSRF_INVALID", "Your security token has expired. Please retry the request.");
            return;
        }
        await _next(context);
    }

    private static Task Reject(HttpContext context, string code, string message)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.Headers.CacheControl = "no-store";
        return context.Response.WriteAsJsonAsync(new { code, message, traceId = context.TraceIdentifier }, context.RequestAborted);
    }
}
