using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Models;
using System.Text.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace SecureClientPortal.Backend.Controllers;

public record LoginRequest(string Email, string Password);

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly PortalDbContext _db;
    private readonly JwtOptions _jwtOptions;

    public AuthController(PortalDbContext db, IOptions<JwtOptions> jwtOptions)
    {
        _db = db;
        _jwtOptions = jwtOptions.Value;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(x => x.Email == email);
        if (user is null || !PasswordHasher.Verify(request.Password, user.PasswordHash))
        {
            return Unauthorized(new { error = "Invalid credentials" });
        }

        var securityStatus = UserSecurityProfile.GetStatus(user.SecurityJson);
        if (securityStatus is "disabled" or "locked")
        {
            return Unauthorized(new { error = "User access is disabled." });
        }

        var role = await _db.RoleDefinitions.FirstOrDefaultAsync(x => x.Name == user.Role);
        if (role is null || !role.IsActive)
        {
            return Unauthorized(new { error = "User role is inactive." });
        }

        var permissions = RolePermissions.ParsePermissions(role.PermissionsJson, role.Name);
        var scope = RolePermissions.NormalizeScope(role.Scope);
        var jwtId = Guid.NewGuid().ToString("N");
        user.UpdatedAtUtc = DateTime.UtcNow;

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, jwtId),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Role, user.Role),
            new("role_scope", scope),
        };
        foreach (var permission in permissions)
        {
            claims.Add(new Claim("permission", permission));
        }

        if (scope == "client")
        {
            try
            {
                var clientIds = System.Text.Json.JsonSerializer.Deserialize<string[]>(user.ClientIdsJson)
                    ?.Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray() ?? [];
                foreach (var clientId in clientIds)
                {
                    claims.Add(new Claim("client_id", clientId));
                }
            }
            catch
            {
                // Keep login working even if legacy client scope JSON is malformed.
            }
        }
        else if (scope == "accountant" || scope == "admin")
        {
            var assignedClientIds = await _db.ClientAssignments
                .Where(x => x.AccountantUserId == user.Id)
                .Select(x => x.ClientId)
                .Distinct()
                .ToListAsync();
            foreach (var clientId in assignedClientIds)
            {
                claims.Add(new Claim("assigned_client_id", clientId));
            }
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtOptions.SigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = DateTime.UtcNow.AddMinutes(_jwtOptions.ExpiresMinutes);
        var session = new UserSession
        {
            Id = $"sess_{Guid.NewGuid():N}",
            UserId = user.Id,
            JwtId = jwtId,
            IssuedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = expires,
            ClientIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = Request.Headers.UserAgent.ToString()
        };

        var token = new JwtSecurityToken(
            issuer: _jwtOptions.Issuer,
            audience: _jwtOptions.Audience,
            claims: claims,
            expires: expires,
            signingCredentials: creds
        );

        _db.UserSessions.Add(session);
        await _db.SaveChangesAsync();
        await _db.WriteAuditLogAsync(
            user.Id,
            user.Role,
            "auth.login",
            "user",
            user.Id,
            null,
            JsonSerializer.Serialize(new { user.Email, user.Role, sessionId = session.Id }));

        return Ok(new
        {
            token = new JwtSecurityTokenHandler().WriteToken(token),
            expiresAtUtc = expires,
            user = new { user.Id, user.FullName, user.Email, user.Role, roleScope = scope },
            session = new { session.Id, session.ExpiresAtUtc }
        });
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        var jwtId = User.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
        if (string.IsNullOrWhiteSpace(jwtId))
        {
            return NoContent();
        }

        var session = await _db.UserSessions.FirstOrDefaultAsync(x => x.JwtId == jwtId);
        if (session is null || session.RevokedAtUtc is not null)
        {
            return NoContent();
        }

        session.RevokedAtUtc = DateTime.UtcNow;
        session.RevokedReason = "logout";
        await _db.SaveChangesAsync();
        await _db.WriteAuditLogAsync(
            User,
            "auth.logout",
            "user_session",
            session.Id,
            null,
            JsonSerializer.Serialize(new { session.UserId, session.JwtId }));

        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var userId = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        var user = await _db.Users.FirstOrDefaultAsync(x => x.Id == userId, ct);
        if (user is null)
        {
            return NotFound();
        }

        var role = await _db.RoleDefinitions.FirstOrDefaultAsync(x => x.Name == user.Role, ct);
        var accessibleClientIds = await User.GetAccessibleClientIdsAsync(_db, ct);
        return Ok(new
        {
            user = new
            {
                user.Id,
                user.FullName,
                user.Email,
                user.Role,
                roleScope = role?.Scope ?? RolePermissions.ScopeForRole(user.Role),
                permissions = role is null
                    ? RolePermissions.ForRole(user.Role)
                    : RolePermissions.ParsePermissions(role.PermissionsJson, role.Name),
                clientIds = accessibleClientIds.OrderBy(x => x).ToArray()
            }
        });
    }
}
