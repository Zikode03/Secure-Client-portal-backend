using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application;
using SecureClientPortal.Backend.Application.Assignments;
using SecureClientPortal.Backend.Application.FirmManagement;
using SecureClientPortal.Backend.Application.Roles;
using Microsoft.IdentityModel.Tokens;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Storage;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? Environment.GetEnvironmentVariable("DB_CONNECTION_STRING");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("Database connection is missing. Set ConnectionStrings:DefaultConnection or DB_CONNECTION_STRING.");
}

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.Section));
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.Section));
builder.Services.Configure<PortalLinksOptions>(builder.Configuration.GetSection(PortalLinksOptions.Section));
builder.Services.Configure<AccessEmailOptions>(builder.Configuration.GetSection(AccessEmailOptions.Section));
var jwt = builder.Configuration.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();
var jwtSigningKeyFromEnv = Environment.GetEnvironmentVariable("JWT_SIGNING_KEY");
if (!string.IsNullOrWhiteSpace(jwtSigningKeyFromEnv))
{
    jwt.SigningKey = jwtSigningKeyFromEnv;
}
var configuredCorsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
var defaultCorsOrigins = new[]
{
    "http://localhost:5173",
    "http://127.0.0.1:5173",
    "http://localhost:4173",
    "http://127.0.0.1:4173"
};
var corsOrigins = configuredCorsOrigins
    .Concat(defaultCorsOrigins)
    .Where(origin => !string.IsNullOrWhiteSpace(origin))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
        policy
            .WithOrigins(corsOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod());
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
builder.Services.AddScoped<IFileStorage, LocalFileStorage>();
builder.Services.AddScoped<IAccessEmailSender, AccessEmailSender>();
builder.Services.AddSingleton<IAccessLinkBuilder, AccessLinkBuilder>();
builder.Services.AddSingleton<ICurrentUserContextFactory, CurrentUserContextFactory>();
builder.Services.AddScoped<IRoleService, RoleService>();
builder.Services.AddScoped<IFirmManagementService, FirmManagementService>();
builder.Services.AddScoped<IAssignmentService, AssignmentService>();

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
            OnTokenValidated = async context =>
            {
                var principal = context.Principal;
                var userId = principal?.GetUserId();
                var jwtId = principal?.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;

                if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(jwtId))
                {
                    context.Fail("Session is invalid.");
                    return;
                }

                var db = context.HttpContext.RequestServices.GetRequiredService<PortalDbContext>();
                var user = await db.Users.FirstOrDefaultAsync(x => x.Id == userId, context.HttpContext.RequestAborted);
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

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("Frontend");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

await SeedData.InitializeAsync(app.Services);

app.Run();

static string PartitionKey(HttpContext httpContext)
{
    var path = httpContext.Request.Path.Value ?? "unknown";
    var remoteIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    return $"{path}:{remoteIp}";
}
