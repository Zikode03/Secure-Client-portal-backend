using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SecureClientPortal.Backend.Api.Security;
using SecureClientPortal.Backend.Auth;

namespace SecureClientPortal.Backend.Tests;

public sealed class TransportSecurityTests
{
    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task EveryMutationRequiresCsrfHeader(string method)
    {
        using var services = Services();
        var context = Context(services, method);
        var called = false;
        await new CsrfProtectionMiddleware(_ => { called = true; return Task.CompletedTask; }, ["https://portal.example"])
            .InvokeAsync(context, services.GetRequiredService<IAntiforgery>());
        Assert.False(called);
        Assert.Equal(403, context.Response.StatusCode);
        Assert.Contains("CSRF_INVALID", await Body(context));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ValidAnonymousAndAuthenticatedTokensPermitRequests(bool authenticated)
    {
        using var services = Services();
        var context = Context(services, "POST", authenticated ? "user-one" : null);
        IssueToken(services, context, authenticated ? "user-one" : null);
        context.Request.ContentType = "multipart/form-data; boundary=test";
        var called = false;
        await new CsrfProtectionMiddleware(_ => { called = true; return Task.CompletedTask; }, ["https://portal.example"])
            .InvokeAsync(context, services.GetRequiredService<IAntiforgery>());
        Assert.True(called);
    }

    [Fact]
    public async Task TokenFromDifferentIdentityIsRejected()
    {
        using var services = Services();
        var context = Context(services, "POST", "user-two");
        IssueToken(services, context, "user-one");
        await new CsrfProtectionMiddleware(_ => throw new Exception("Action should not run"), ["https://portal.example"])
            .InvokeAsync(context, services.GetRequiredService<IAntiforgery>());
        Assert.Equal(403, context.Response.StatusCode);
        Assert.Contains("CSRF_INVALID", await Body(context));
    }

    [Fact]
    public async Task UntrustedOriginIsRejectedEvenWithValidToken()
    {
        using var services = Services();
        var context = Context(services, "POST");
        IssueToken(services, context);
        context.Request.Headers.Origin = "https://attacker.example";
        await new CsrfProtectionMiddleware(_ => throw new Exception("Action should not run"), ["https://portal.example"])
            .InvokeAsync(context, services.GetRequiredService<IAntiforgery>());
        Assert.Equal(403, context.Response.StatusCode);
        Assert.Contains("ORIGIN_NOT_ALLOWED", await Body(context));
    }

    [Fact]
    public async Task MalformedRefererIsRejected()
    {
        using var services = Services();
        var context = Context(services, "POST");
        context.Request.Headers.Remove("Origin");
        context.Request.Headers.Referer = "not-a-url";
        IssueToken(services, context);
        await new CsrfProtectionMiddleware(_ => throw new Exception("Action should not run"), ["https://portal.example"])
            .InvokeAsync(context, services.GetRequiredService<IAntiforgery>());
        Assert.Equal(403, context.Response.StatusCode);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    public async Task SafeMethodsDoNotRequireTokens(string method)
    {
        using var services = Services();
        var context = Context(services, method);
        var called = false;
        await new CsrfProtectionMiddleware(_ => { called = true; return Task.CompletedTask; }, [])
            .InvokeAsync(context, services.GetRequiredService<IAntiforgery>());
        Assert.True(called);
    }

    [Theory]
    [InlineData("10.1.0.2", true)]
    [InlineData("203.0.113.5", false)]
    public async Task OnlyTrustedProxiesCanChangeSchemeAndClientIp(string peer, bool trusted)
    {
        var config = Config(new() { ["Security:TrustedProxies:0"] = "10.1.0.2" });
        var options = PortalTransportSecurity.BuildForwardedHeaders(config);
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("portal.example");
        context.Request.Headers["X-Forwarded-Proto"] = "https";
        context.Request.Headers["X-Forwarded-For"] = "198.51.100.7";
        context.Request.Headers["X-Forwarded-Host"] = "attacker.example";
        await new ForwardedHeadersMiddleware(_ => Task.CompletedTask, NullLoggerFactory.Instance, Options.Create(options)).Invoke(context);
        Assert.Equal(trusted ? "https" : "http", context.Request.Scheme);
        Assert.Equal(trusted ? "198.51.100.7" : peer, context.Connection.RemoteIpAddress!.ToString());
        Assert.Equal("portal.example", context.Request.Host.Value);
    }

    [Theory]
    [InlineData("Security:TrustedProxies:0", "*")]
    [InlineData("Security:TrustedProxies:0", "0.0.0.0")]
    [InlineData("Security:TrustedNetworks:0", "0.0.0.0/0")]
    [InlineData("Security:TrustedNetworks:0", "::/0")]
    [InlineData("Security:ForwardLimit", "0")]
    public void UnsafeProxyConfigurationFails(string key, string value)
    {
        Assert.Throws<InvalidOperationException>(() => PortalTransportSecurity.BuildForwardedHeaders(Config(new() { [key] = value })));
    }

    [Fact]
    public async Task ProductionRedirectsHttpReadsAndRejectsHttpWrites()
    {
        using var services = Services();
        var builder = new ApplicationBuilder(services);
        builder.UseMiddleware<ApiSecurityHeadersMiddleware>().UseHsts().UseHttpsRedirection();
        builder.Run(_ => Task.CompletedTask);
        var pipeline = builder.Build();
        var read = Context(services, "GET");
        read.Request.Scheme = "http";
        await pipeline(read);
        Assert.Equal(308, read.Response.StatusCode);
        Assert.Equal("https://portal.example/api/documents", read.Response.Headers.Location.ToString());
        var write = Context(services, "POST");
        write.Request.Scheme = "http";
        await pipeline(write);
        Assert.Equal(400, write.Response.StatusCode);
        Assert.Contains("HTTPS_REQUIRED", await Body(write));
        var secure = Context(services, "GET");
        await pipeline(secure);
        Assert.Contains("max-age=", secure.Response.Headers.StrictTransportSecurity.ToString());
        Assert.Equal("DENY", secure.Response.Headers["X-Frame-Options"]);
        Assert.Equal("nosniff", secure.Response.Headers["X-Content-Type-Options"]);
    }

    [Fact]
    public async Task UnexpectedErrorsAreGenericAndHaveSecurityHeaders()
    {
        using var services = Services();
        var context = Context(services, "GET");
        await new ApiExceptionMiddleware(_ => throw new InvalidOperationException("db-password=secret"), NullLogger<ApiExceptionMiddleware>.Instance).InvokeAsync(context);
        Assert.Equal(500, context.Response.StatusCode);
        var body = await Body(context);
        Assert.Contains("INTERNAL_ERROR", body);
        Assert.DoesNotContain("db-password", body);
        Assert.Contains("traceId", body);
        Assert.Equal("no-store", context.Response.Headers.CacheControl);
        Assert.Equal("no-referrer", context.Response.Headers["Referrer-Policy"]);
    }

    [Fact]
    public void ProductionAuthAndAntiforgeryCookiesAreSecureAndHttpOnly()
    {
        using var services = Services();
        var context = Context(services, "GET");
        AuthCookies.Write(context, "access", DateTime.UtcNow.AddMinutes(5), "refresh", DateTime.UtcNow.AddDays(1), false);
        services.GetRequiredService<IAntiforgery>().GetAndStoreTokens(context);
        var cookies = context.Response.Headers.SetCookie.ToArray();
        Assert.Contains(cookies, cookie => cookie!.StartsWith("__Host-scp_csrf="));
        Assert.All(cookies, cookie =>
        {
            Assert.Contains("secure", cookie!);
            Assert.Contains("httponly", cookie!);
            Assert.Contains("samesite=strict", cookie!);
            Assert.DoesNotContain("domain=", cookie!);
        });
    }

    private static void IssueToken(IServiceProvider services, HttpContext destination, string? user = null)
    {
        var issuing = Context(services, "GET", user);
        var tokens = services.GetRequiredService<IAntiforgery>().GetAndStoreTokens(issuing);
        destination.Request.Headers.Cookie = issuing.Response.Headers.SetCookie[0]!.Split(';')[0];
        destination.Request.Headers[CsrfProtectionMiddleware.HeaderName] = tokens.RequestToken;
    }
    private static DefaultHttpContext Context(IServiceProvider services, string method, string? user = null)
    {
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("portal.example");
        context.Request.Path = "/api/documents";
        context.Request.Method = method;
        context.Request.Headers.Origin = "https://portal.example";
        context.Response.Body = new MemoryStream();
        if (user is not null) context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user), new Claim(ClaimTypes.Name, user)], "test"));
        return context;
    }
    private static async Task<string> Body(HttpContext context)
    {
        context.Response.Body.Position = 0;
        return await new StreamReader(context.Response.Body).ReadToEndAsync(TestContext.Current.CancellationToken);
    }
    private static IConfiguration Config(Dictionary<string, string?> values) => new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    private static ServiceProvider Services()
    {
        var environment = new TestEnvironment();
        var config = Config(new());
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<IHostEnvironment>(environment).AddSingleton<IWebHostEnvironment>(environment).AddSingleton(config);
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        services.AddPortalTransportSecurity(config, environment);
        return services.BuildServiceProvider();
    }
    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "SecurityTests";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = ".";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
