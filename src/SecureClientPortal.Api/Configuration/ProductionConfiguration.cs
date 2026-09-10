using System.Net;
using System.Net.Mail;
using SecureClientPortal.Backend.Auth;

namespace SecureClientPortal.Backend.Api.Configuration;

public static class ProductionConfiguration
{
    public static void Validate(IConfiguration configuration, IHostEnvironment environment, string? connectionString, JwtOptions jwt)
    {
        if (environment.IsDevelopment()) return;
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(connectionString)) errors.Add("Database connection is required.");
        if (string.IsNullOrWhiteSpace(jwt.SigningKey) || jwt.SigningKey.Trim().Length < 32 ||
            jwt.SigningKey.Contains("CHANGE", StringComparison.OrdinalIgnoreCase))
            errors.Add("JWT_SIGNING_KEY must contain at least 32 characters and cannot be a placeholder.");
        if (string.IsNullOrWhiteSpace(jwt.Issuer) || string.IsNullOrWhiteSpace(jwt.Audience) || jwt.ExpiresMinutes <= 0)
            errors.Add("Jwt issuer, audience and positive expiry are required.");

        var frontendUrl = configuration["PortalLinks:FrontendBaseUrl"];
        if (!IsHttpsUrl(frontendUrl, out var frontend))
            errors.Add("PortalLinks:FrontendBaseUrl must be a non-local HTTPS URL.");
        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        if (origins.Length == 0 || origins.Any(origin => !IsHttpsUrl(origin, out var uri) ||
                uri!.AbsolutePath != "/" || origin != uri.GetLeftPart(UriPartial.Authority)))
            errors.Add("Cors:AllowedOrigins must contain explicit HTTPS origins without paths or trailing slashes.");
        if (frontend is not null && !origins.Contains(frontend.GetLeftPart(UriPartial.Authority), StringComparer.OrdinalIgnoreCase))
            errors.Add("Cors:AllowedOrigins must include the frontend origin.");

        var hosts = configuration["AllowedHosts"]?.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) ?? [];
        if (hosts.Length == 0 || hosts.Any(host => !IsHost(host)))
            errors.Add("AllowedHosts must list explicit non-local API hostnames separated by semicolons; wildcards and URLs are not allowed.");

        var email = configuration.GetSection(AccessEmailOptions.Section).Get<AccessEmailOptions>() ?? new();
        if (!email.Enabled || !string.Equals(email.DeliveryMode, "smtp", StringComparison.OrdinalIgnoreCase))
            errors.Add("AccessEmail must be enabled with DeliveryMode=smtp.");
        if (!IsHost(email.SmtpHost) || email.SmtpPort is < 1 or > 65535 || !email.UseSsl)
            errors.Add("AccessEmail requires a non-local SMTP host, valid port and UseSsl=true.");
        if (!MailAddress.TryCreate(email.FromEmail, out var address) || !IsHost(address.Host) || address.Host.EndsWith(".invalid", StringComparison.OrdinalIgnoreCase))
            errors.Add("AccessEmail:FromEmail must be a valid sender address, not a development placeholder.");
        if (!string.IsNullOrWhiteSpace(email.SmtpUsername) && string.IsNullOrWhiteSpace(email.SmtpPassword))
            errors.Add("AccessEmail:SmtpPassword is required when SmtpUsername is supplied.");

        if (errors.Count > 0)
            throw new InvalidOperationException("Production configuration is invalid:\n" + string.Join("\n", errors));
    }

    private static bool IsHttpsUrl(string? value, out Uri? uri)
    {
        uri = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed) || parsed.Scheme != Uri.UriSchemeHttps ||
            !IsHost(parsed.Host) || parsed.UserInfo.Length > 0 || parsed.Query.Length > 0 || parsed.Fragment.Length > 0)
            return false;
        uri = parsed;
        return true;
    }

    private static bool IsHost(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim() || value.Contains('*')) return false;
        var host = value.TrimEnd('.');
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)) return false;
        if (IPAddress.TryParse(host.Trim('[', ']'), out var ip))
            return !IPAddress.IsLoopback(ip) && !ip.Equals(IPAddress.Any) && !ip.Equals(IPAddress.IPv6Any);
        return Uri.CheckHostName(host) == UriHostNameType.Dns && host.Contains('.');
    }
}
