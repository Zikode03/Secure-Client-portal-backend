using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace SecureClientPortal.Backend.Api.Security;

public static class PortalTransportSecurity
{
    public static IServiceCollection AddPortalTransportSecurity(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        if (!environment.IsDevelopment() && configuration.GetValue<bool>("ASPNETCORE_FORWARDEDHEADERS_ENABLED"))
            throw new InvalidOperationException("Use Security:TrustedProxies or Security:TrustedNetworks instead of ASPNETCORE_FORWARDEDHEADERS_ENABLED.");
        var forwarded = BuildForwardedHeaders(configuration);
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = forwarded.ForwardedHeaders;
            options.ForwardLimit = forwarded.ForwardLimit;
            options.KnownProxies.Clear();
            options.KnownNetworks.Clear();
            foreach (var proxy in forwarded.KnownProxies) options.KnownProxies.Add(proxy);
            foreach (var network in forwarded.KnownNetworks) options.KnownNetworks.Add(network);
        });
        services.AddHttpsRedirection(options =>
        {
            options.HttpsPort = configuration.GetValue<int?>("Security:HttpsPort") ?? 443;
            if (options.HttpsPort is < 1 or > 65535) throw new InvalidOperationException("Security:HttpsPort is invalid.");
            options.RedirectStatusCode = StatusCodes.Status308PermanentRedirect;
        });
        services.AddHsts(options =>
        {
            options.MaxAge = TimeSpan.FromDays(30);
            options.IncludeSubDomains = false;
            options.Preload = false;
        });
        services.AddAntiforgery(options =>
        {
            options.HeaderName = CsrfProtectionMiddleware.HeaderName;
            options.Cookie.Name = environment.IsDevelopment() ? "scp_csrf" : "__Host-scp_csrf";
            options.Cookie.HttpOnly = true;
            options.Cookie.Path = "/";
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.SuppressXFrameOptionsHeader = true;
        });
        return services;
    }

    public static ForwardedHeadersOptions BuildForwardedHeaders(IConfiguration configuration)
    {
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            ForwardLimit = configuration.GetValue<int?>("Security:ForwardLimit") ?? 1
        };
        if (options.ForwardLimit is < 1 or > 5) throw new InvalidOperationException("Security:ForwardLimit must be between 1 and 5.");
        options.KnownProxies.Clear();
        options.KnownNetworks.Clear();
        var proxies = configuration.GetSection("Security:TrustedProxies").Get<string[]>() ?? ["127.0.0.1", "::1"];
        foreach (var value in proxies)
        {
            if (!IPAddress.TryParse(value, out var address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
                throw new InvalidOperationException("Security:TrustedProxies must contain explicit IP addresses.");
            options.KnownProxies.Add(address);
        }
        foreach (var value in configuration.GetSection("Security:TrustedNetworks").Get<string[]>() ?? [])
        {
            var parts = value.Split('/');
            if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var address) || !int.TryParse(parts[1], out var prefix) ||
                prefix <= 0 || prefix > address.GetAddressBytes().Length * 8)
                throw new InvalidOperationException("Security:TrustedNetworks must contain specific CIDR networks; /0 is forbidden.");
            options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(address, prefix));
        }
        if (options.KnownProxies.Count == 0 && options.KnownNetworks.Count == 0)
            throw new InvalidOperationException("The proxy trust list cannot be empty.");
        return options;
    }
}
