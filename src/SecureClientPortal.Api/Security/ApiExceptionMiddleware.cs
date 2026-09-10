using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Models;

namespace SecureClientPortal.Backend.Api.Security;

public sealed class ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try { await next(context); }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        catch (Exception ex) when (!context.Response.HasStarted)
        {
            var (status, code, message) = ex switch
            {
                AppValidationException validation => (400, "VALIDATION_ERROR", validation.Message),
                DomainRuleException domain => (400, "DOMAIN_RULE", domain.Message),
                BadHttpRequestException bad => (bad.StatusCode, "INVALID_REQUEST", "The request could not be processed."),
                _ => (500, "INTERNAL_ERROR", "An unexpected error occurred. Please try again or contact support.")
            };
            if (status >= 500) logger.LogError(ex, "API request failed. TraceId={TraceId}", context.TraceIdentifier);
            var hsts = context.Response.Headers.StrictTransportSecurity;
            context.Response.Clear();
            ApiSecurityHeadersMiddleware.Apply(context);
            if (hsts.Count > 0) context.Response.Headers.StrictTransportSecurity = hsts;
            context.Response.StatusCode = status;
            await context.Response.WriteAsJsonAsync(new { code, message, traceId = context.TraceIdentifier }, context.RequestAborted);
        }
    }
}
