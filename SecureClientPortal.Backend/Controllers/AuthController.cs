using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using System.Text.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Nodes;

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

        var securityStatus = GetSecurityStatus(user.SecurityJson);
        if (securityStatus is "disabled" or "locked")
        {
            return Unauthorized(new { error = "User access is disabled." });
        }

        user.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _db.WriteAuditLogAsync(
            user.Id,
            user.Role,
            "auth.login",
            "user",
            user.Id,
            null,
            JsonSerializer.Serialize(new { user.Email, user.Role }));

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Role, user.Role),
        };

        var normalizedRole = user.Role.Trim().ToLowerInvariant();
        if (normalizedRole != user.Role)
        {
            claims.Add(new Claim(ClaimTypes.Role, normalizedRole));
        }

        if (normalizedRole == "client")
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
        else if (normalizedRole == "accountant")
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

        var token = new JwtSecurityToken(
            issuer: _jwtOptions.Issuer,
            audience: _jwtOptions.Audience,
            claims: claims,
            expires: expires,
            signingCredentials: creds
        );

        return Ok(new
        {
            token = new JwtSecurityTokenHandler().WriteToken(token),
            expiresAtUtc = expires,
            user = new { user.Id, user.FullName, user.Email, user.Role }
        });
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me()
    {
        var userId = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        var user = await _db.Users.FirstOrDefaultAsync(x => x.Id == userId);
        if (user is null)
        {
            return NotFound();
        }

        var accessibleClientIds = await User.GetAccessibleClientIdsAsync(_db);
        return Ok(new
        {
            user = new
            {
                user.Id,
                user.FullName,
                user.Email,
                user.Role,
                permissions = RolePermissions.ForRole(user.Role),
                clientIds = accessibleClientIds.OrderBy(x => x).ToArray()
            }
        });
    }

    private static string GetSecurityStatus(string? securityJson)
    {
        if (string.IsNullOrWhiteSpace(securityJson))
        {
            return "active";
        }

        try
        {
            var node = JsonNode.Parse(securityJson);
            return node?["status"]?.GetValue<string>()?.Trim().ToLowerInvariant() ?? "active";
        }
        catch
        {
            return "active";
        }
    }
}
