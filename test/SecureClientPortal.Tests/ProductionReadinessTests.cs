using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using SecureClientPortal.Backend.Api.Configuration;
using SecureClientPortal.Backend.Api.Modules.Platform;
using SecureClientPortal.Backend.Application.Modules.Platform;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Models;
using System.Text.Json;

namespace SecureClientPortal.Backend.Tests;

public sealed class ProductionReadinessTests
{
    private static readonly JwtOptions ValidJwt = new() { SigningKey = "n0n-default-signing-key-7dac0e50687a49e4" };

    private static Dictionary<string, string?> ValidSettings() => new()
    {
        ["PortalLinks:FrontendBaseUrl"] = "https://portal.example.com",
        ["Cors:AllowedOrigins:0"] = "https://portal.example.com",
        ["AllowedHosts"] = "api.example.com",
        ["AccessEmail:Enabled"] = "true",
        ["AccessEmail:DeliveryMode"] = "smtp",
        ["AccessEmail:SmtpHost"] = "smtp.example.com",
        ["AccessEmail:SmtpPort"] = "587",
        ["AccessEmail:UseSsl"] = "true",
        ["AccessEmail:FromEmail"] = "portal@example.com"
    };

    [Fact]
    public void ExplicitProductionConfigurationIsAccepted()
    {
        ProductionConfiguration.Validate(Config(ValidSettings()), Env(Environments.Production), "configured-connection", ValidJwt);
    }

    [Theory]
    [InlineData("AllowedHosts", "*")]
    [InlineData("AllowedHosts", "api.example.com;*.example.com")]
    [InlineData("AllowedHosts", "localhost")]
    [InlineData("PortalLinks:FrontendBaseUrl", "http://portal.example.com")]
    [InlineData("PortalLinks:FrontendBaseUrl", "https://localhost:5173")]
    [InlineData("PortalLinks:FrontendBaseUrl", "https://[::1]")]
    [InlineData("Cors:AllowedOrigins:0", "https://other.example.com")]
    [InlineData("Cors:AllowedOrigins:0", "https://portal.example.com/path")]
    [InlineData("Cors:AllowedOrigins:0", "https://portal.example.com/")]
    [InlineData("AccessEmail:Enabled", "false")]
    [InlineData("AccessEmail:DeliveryMode", "log")]
    [InlineData("AccessEmail:SmtpHost", "")]
    [InlineData("AccessEmail:SmtpHost", "127.0.0.1")]
    [InlineData("AccessEmail:SmtpPort", "0")]
    [InlineData("AccessEmail:UseSsl", "false")]
    [InlineData("AccessEmail:FromEmail", "no-reply@example.invalid")]
    [InlineData("AccessEmail:SmtpUsername", "user-with-no-password")]
    public void UnsafeSettingsFailBeforeStartup(string key, string value)
    {
        var settings = ValidSettings();
        settings[key] = value;
        Assert.Throws<InvalidOperationException>(() => ProductionConfiguration.Validate(Config(settings), Env(Environments.Production), "configured-connection", ValidJwt));
    }

    [Fact]
    public void MissingSecretsAndPlaceholderSigningKeysAreRejected()
    {
        var configuration = Config(ValidSettings());
        Assert.Throws<InvalidOperationException>(() => ProductionConfiguration.Validate(configuration, Env(Environments.Production), null, ValidJwt));
        Assert.Throws<InvalidOperationException>(() => ProductionConfiguration.Validate(configuration, Env(Environments.Production), "configured-connection", new JwtOptions()));
    }

    [Fact]
    public void DevelopmentAllowsLocalConfigurationButStagingDoesNot()
    {
        var empty = Config(new());
        ProductionConfiguration.Validate(empty, Env(Environments.Development), null, new JwtOptions());
        Assert.Throws<InvalidOperationException>(() => ProductionConfiguration.Validate(empty, Env(Environments.Staging), null, new JwtOptions()));
    }

    [Fact]
    public async Task ReferenceSeedingDoesNotCreateDemoRecordsAndPreservesDisabledRoles()
    {
        using var provider = Services();
        await SeedData.InitializeAsync(provider);
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            Assert.Empty(await db.Users.ToListAsync(TestContext.Current.CancellationToken));
            Assert.Empty(await db.Clients.ToListAsync(TestContext.Current.CancellationToken));
            Assert.Empty(await db.MonthlyPacks.ToListAsync(TestContext.Current.CancellationToken));
            Assert.NotEmpty(await db.RoleDefinitions.ToListAsync(TestContext.Current.CancellationToken));
            Assert.NotEmpty(await db.ComplianceCategories.ToListAsync(TestContext.Current.CancellationToken));
            var admin = await db.RoleDefinitions.FirstAsync(x => x.Name == "admin", TestContext.Current.CancellationToken);
            admin.SetActivation(false);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        await SeedData.InitializeAsync(provider);
        await SeedData.EnsureNoDemoDataAsync(provider);
        using var checkScope = provider.CreateScope();
        var check = checkScope.ServiceProvider.GetRequiredService<PortalDbContext>();
        Assert.False((await check.RoleDefinitions.FirstAsync(x => x.Name == "admin", TestContext.Current.CancellationToken)).IsActive);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task DemoSeedCannotBeCalledOutsideDevelopment(string environment)
    {
        using var provider = Services();
        await Assert.ThrowsAsync<InvalidOperationException>(() => SeedData.InitializeDevelopmentAsync(provider, Env(environment)));
    }

    [Fact]
    public async Task DevelopmentSeedDoesNotResetExistingPasswordsAndProductionRejectsItsRecords()
    {
        using var provider = Services();
        await SeedData.InitializeAsync(provider);
        await SeedData.InitializeDevelopmentAsync(provider, Env(Environments.Development));
        var changedHash = PasswordHasher.Hash("Changed-long-password!2026");
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var admin = await db.Users.FirstAsync(x => x.Email == "admin@secureportal.local", TestContext.Current.CancellationToken);
            admin.SetPasswordHash(changedHash);
            admin.SetSecurityStatus(SecurityStatus.Disabled);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        await SeedData.InitializeDevelopmentAsync(provider, Env(Environments.Development));
        await Assert.ThrowsAsync<InvalidOperationException>(() => SeedData.EnsureNoDemoDataAsync(provider));
        using var checkScope = provider.CreateScope();
        var check = checkScope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var user = await check.Users.FirstAsync(x => x.Email == "admin@secureportal.local", TestContext.Current.CancellationToken);
        Assert.Equal(changedHash, user.PasswordHash);
        Assert.Equal("disabled", UserSecurityProfile.GetStatus(user.SecurityJson));
    }

    [Fact]
    public async Task PublicDatabaseHealthNeverReturnsExceptionDetails()
    {
        var controller = new HealthController(new FailedHealth());
        var result = Assert.IsType<ObjectResult>(await controller.GetDatabaseHealth(TestContext.Current.CancellationToken));
        Assert.Equal(503, result.StatusCode);
        var json = JsonSerializer.Serialize(result.Value);
        Assert.DoesNotContain("sensitive-server", json);
        Assert.DoesNotContain("secret-password", json);
        Assert.Contains("Database health check failed", json);
    }

    private sealed class FailedHealth : IHealthService
    {
        public object GetServiceHealth() => new { ok = true };
        public Task<(bool ok, string database, string? error)> GetDatabaseHealthAsync(CancellationToken ct = default) =>
            Task.FromResult((false, "sensitive-server", (string?)"secret-password"));
    }

    private static ServiceProvider Services()
    {
        var name = Guid.NewGuid().ToString();
        return new ServiceCollection().AddDbContext<PortalDbContext>(options => options.UseInMemoryDatabase(name)).BuildServiceProvider();
    }

    private static IConfiguration Config(Dictionary<string, string?> settings) => new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    private static IHostEnvironment Env(string name) => new TestEnvironment { EnvironmentName = name };
    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Phase1Tests";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
