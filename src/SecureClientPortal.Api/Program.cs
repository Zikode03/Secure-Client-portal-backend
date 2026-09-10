using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application;
using SecureClientPortal.Backend.Application.Identity;
using SecureClientPortal.Backend.Application.Modules.Assignments;
using SecureClientPortal.Backend.Application.Modules.AuditLogs;
using SecureClientPortal.Backend.Application.Modules.Auth;
using SecureClientPortal.Backend.Application.Modules.Clients;
using SecureClientPortal.Backend.Application.Modules.Compliance;
using SecureClientPortal.Backend.Application.Modules.FirmManagement;
using SecureClientPortal.Backend.Application.Modules.Platform;
using SecureClientPortal.Backend.Application.Modules.Reports;
using SecureClientPortal.Backend.Application.Modules.UsersRoles;
using SecureClientPortal.Backend.Application.Modules.Requests;
using SecureClientPortal.Backend.Infrastructure.DependencyInjection;
using SecureClientPortal.Backend.Infrastructure.Modules.Documents.Storage;
using SecureClientPortal.Backend.Infrastructure.Modules.Platform;
using Microsoft.IdentityModel.Tokens;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Threading.RateLimiting;
using SecureClientPortal.Backend.Api.Configuration;
using SecureClientPortal.Backend.Api.Security;
using Microsoft.AspNetCore.Antiforgery;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration["DB_CONNECTION_STRING"];
if (string.IsNullOrWhiteSpace(connectionString))
    connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("Database connection is missing. Set ConnectionStrings:DefaultConnection or DB_CONNECTION_STRING.");
}

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.Section));
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.Section));
builder.Services.Configure<PortalLinksOptions>(builder.Configuration.GetSection(PortalLinksOptions.Section));
builder.Services.Configure<AccessEmailOptions>(builder.Configuration.GetSection(AccessEmailOptions.Section));
builder.Services.Configure<AutomationOptions>(builder.Configuration.GetSection(AutomationOptions.Section));
var configuredStorage = builder.Configuration.GetSection(StorageOptions.Section).Get<StorageOptions>() ?? new StorageOptions();
var keyRingPath = Path.GetFullPath(
    Path.IsPathRooted(configuredStorage.KeyRingPath)
        ? configuredStorage.KeyRingPath
        : Path.Combine(builder.Environment.ContentRootPath, configuredStorage.KeyRingPath));
var jwt = builder.Configuration.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();
var jwtSigningKeyFromEnv = Environment.GetEnvironmentVariable("JWT_SIGNING_KEY");
if (!string.IsNullOrWhiteSpace(jwtSigningKeyFromEnv))
{
    jwt.SigningKey = jwtSigningKeyFromEnv;
    builder.Services.PostConfigure<JwtOptions>(options => options.SigningKey = jwtSigningKeyFromEnv);
}
ProductionConfiguration.Validate(builder.Configuration, builder.Environment, connectionString, jwt);
builder.Services.AddPortalTransportSecurity(builder.Configuration, builder.Environment);
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);
Directory.CreateDirectory(keyRingPath);
builder.Services
    .AddDataProtection()
    .SetApplicationName("SecureClientPortal")
    .PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));
var configuredCorsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
var defaultCorsOrigins = new[]
{
    "http://localhost:5173",
    "http://127.0.0.1:5173",
    "http://localhost:4173",
    "http://127.0.0.1:4173"
};
var corsOrigins = configuredCorsOrigins
    .Concat(builder.Environment.IsDevelopment() ? defaultCorsOrigins : [])
    .Where(origin => !string.IsNullOrWhiteSpace(origin))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();
if (corsOrigins.Length == 0)
{
    throw new InvalidOperationException("At least one Cors:AllowedOrigins entry is required.");
}

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
        policy
            .WithOrigins(corsOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = (context, cancellationToken) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        return new ValueTask(context.HttpContext.Response.WriteAsJsonAsync(
            new { code = "RATE_LIMITED", message = "Too many authentication attempts. Please wait and try again." },
            cancellationToken));
    };
    options.AddPolicy("auth-login", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            PartitionKey(httpContext),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 8,
                Window = TimeSpan.FromMinutes(5),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.AddPolicy("auth-recovery", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            PartitionKey(httpContext),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(15),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.AddPolicy("auth-refresh", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            PartitionKey(httpContext),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(5),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.AddPolicy("auth-account", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            PartitionKey(httpContext),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(10),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

builder.Services.AddDbContext<PortalDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddScoped<SecureClientPortal.Backend.Application.Identity.IAccessEmailSender, AccessEmailSender>();
builder.Services.AddSingleton<SecureClientPortal.Backend.Application.Identity.IAccessLinkBuilder, AccessLinkBuilder>();
builder.Services
    .AddPlatformModule()
    .AddAuthModule()
    .AddUsersRolesModule()
    .AddMonthlyPacksModule()
    .AddDocumentModule()
    .AddNotificationsModule()
    .AddClientsModule()
    .AddAssignmentsModule()
    .AddFirmManagementModule()
    .AddRequestModule()
    .AddReviewQueueModule()
    .AddComplianceModule()
    .AddAuditLogsModule()
    .AddReportsModule();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ClockSkew = TimeSpan.FromMinutes(2)
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (string.IsNullOrWhiteSpace(context.Token) &&
                    context.Request.Cookies.TryGetValue(AuthCookies.AccessTokenName, out var cookieToken))
                {
                    context.Token = cookieToken;
                }

                return Task.CompletedTask;
            },
            OnTokenValidated = async context =>
            {
                var principal = context.Principal;
                var userId = principal?.GetUserId();
                var jwtIdValue = principal?.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;

                if (!userId.HasValue || !Guid.TryParse(jwtIdValue, out var jwtId))
                {
                    context.Fail("Session is invalid.");
                    return;
                }

                var db = context.HttpContext.RequestServices.GetRequiredService<PortalDbContext>();
                var user = await db.Users.FirstOrDefaultAsync(x => x.Id == userId.Value, context.HttpContext.RequestAborted);
                if (user is null)
                {
                    context.Fail("User does not exist.");
                    return;
                }

                if (UserSecurityProfile.GetStatus(user.SecurityJson) is "disabled" or "locked" or "invited" or "reset_pending" or "password_reset_required")
                {
                    context.Fail("User access is not active.");
                    return;
                }

                var role = await db.RoleDefinitions.FirstOrDefaultAsync(x => x.Name == user.Role, context.HttpContext.RequestAborted);
                if (role is null || !role.IsActive)
                {
                    context.Fail("Role is inactive.");
                    return;
                }

                var session = await db.UserSessions.FirstOrDefaultAsync(x => x.JwtId == jwtId, context.HttpContext.RequestAborted);
                if (session is null || session.RevokedAtUtc is not null || session.ExpiresAtUtc <= DateTime.UtcNow)
                {
                    context.Fail("Session has expired.");
                }
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireAssertion(ctx => ctx.User.HasPermission("access.admin")));
    options.AddPolicy("AccountantOnly", policy => policy.RequireAssertion(ctx =>
        ctx.User.HasPermission("access.admin") || ctx.User.HasPermission("access.accountant")));
    options.AddPolicy("ClientOrAccountant", policy => policy.RequireAssertion(ctx =>
        ctx.User.HasPermission("access.admin") ||
        ctx.User.HasPermission("access.accountant") ||
        ctx.User.HasPermission("access.client")));
});

var app = builder.Build();

await ApplyDatabaseMigrationsAsync(app.Services, app.Logger);
if (!app.Environment.IsDevelopment())
    await SeedData.EnsureNoDemoDataAsync(app.Services);

app.UseForwardedHeaders();
app.UseMiddleware<ApiExceptionMiddleware>();
app.UseMiddleware<ApiSecurityHeadersMiddleware>();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseRouting();
app.UseCors("Frontend");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<CsrfProtectionMiddleware>((object)corsOrigins);
app.MapGet("/api/auth/csrf", (HttpContext context, IAntiforgery antiforgery) =>
{
    var tokens = antiforgery.GetAndStoreTokens(context);
    context.Response.Headers.CacheControl = "no-store";
    return Results.Ok(new { requestToken = tokens.RequestToken });
}).AllowAnonymous();
app.MapControllers();

await SeedData.InitializeAsync(app.Services);
if (app.Environment.IsDevelopment())
    await SeedData.InitializeDevelopmentAsync(app.Services, app.Environment);
// Seed practical starter templates after the original generic seed. This is idempotent and gives
// company-aware monthly-pack recommendations useful options in every environment.
await BusinessMonthlyPackSeedData.InitializeAsync(app.Services);

app.Run();

static async Task ApplyDatabaseMigrationsAsync(IServiceProvider services, ILogger logger)
{
    await using var scope = services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
    var pendingMigrations = await db.Database.GetPendingMigrationsAsync();
    if (!pendingMigrations.Any())
    {
        return;
    }

    logger.LogInformation("Applying {MigrationCount} pending database migrations.", pendingMigrations.Count());
    await db.Database.MigrateAsync();
}

static string PartitionKey(HttpContext httpContext)
{
    var path = httpContext.Request.Path.Value ?? "unknown";
    var remoteIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    return $"{path}:{remoteIp}";
}
