using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace SecureClientPortal.Backend.Controllers;

public record LoginRequest(string Email, string Password);
public record CompleteInviteRequest(string Email, string Token, string FullName, string Password);
public record ForgotPasswordRequest(string Email);
public record RefreshTokenRequest(string RefreshToken);
public record ChangePasswordRequest(string CurrentPassword, string NextPassword);

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private static readonly string[] SetupTokenPurposes = ["invite", "password_reset"];

    private readonly PortalDbContext _db;
    private readonly JwtOptions _jwtOptions;
    private readonly IAccessEmailSender _accessEmailSender;
    private readonly IAccessLinkBuilder _accessLinkBuilder;

    public AuthController(
        PortalDbContext db,
        IOptions<JwtOptions> jwtOptions,
        IAccessEmailSender accessEmailSender,
        IAccessLinkBuilder accessLinkBuilder)
    {
        _db = db;
        _jwtOptions = jwtOptions.Value;
        _accessEmailSender = accessEmailSender;
        _accessLinkBuilder = accessLinkBuilder;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(x => x.Email == email, ct);
        if (user is null || !PasswordHasher.Verify(request.Password, user.PasswordHash))
        {
            return AuthError(401, "INVALID_CREDENTIALS", "The email or password is incorrect.");
        }

        var securityStatus = UserSecurityProfile.GetStatus(user.SecurityJson);
        if (securityStatus is "invited" or "reset_pending")
        {
            return AuthError(401, "ACCOUNT_SETUP_REQUIRED", "This account still needs to finish setup before it can sign in.");
        }

        if (securityStatus is "password_reset_required")
        {
            return AuthError(401, "PASSWORD_RESET_REQUIRED", "This account needs a password reset before it can sign in.");
        }

        if (securityStatus is "disabled" or "locked")
        {
            return AuthError(401, "ACCOUNT_DISABLED", "This account is disabled.");
        }

        user.UpdatedAtUtc = DateTime.UtcNow;
        var authSession = await IssueAuthResponseAsync(user, null, ct);
        await _db.WriteAuditLogAsync(
            user.Id,
            user.Role,
            "auth.login",
            "user",
            user.Id,
            null,
            JsonSerializer.Serialize(new { user.Email, user.Role, sessionId = authSession.Session.Id }),
            ct);

        return Ok(authSession.Response);
    }

    [HttpPost("complete-invite")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-recovery")]
    public async Task<IActionResult> CompleteInvite([FromBody] CompleteInviteRequest request, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email) || !email.Contains("@"))
        {
            return AuthError(400, "INVALID_INVITE_PAYLOAD", "A valid invite email is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Trim().Length < 8)
        {
            return AuthError(400, "PASSWORD_TOO_SHORT", "Password must be at least 8 characters long.");
        }

        var user = await _db.Users.FirstOrDefaultAsync(x => x.Email == email, ct);
        if (user is null)
        {
            return AuthError(401, "TOKEN_INVALID", "The setup link is invalid or has expired.");
        }

        var securityStatus = UserSecurityProfile.GetStatus(user.SecurityJson);
        if (securityStatus is "disabled" or "locked")
        {
            return AuthError(403, "ACCOUNT_DISABLED", "This account is disabled.");
        }

        var accessToken = await FindActiveAccessTokenAsync(request.Token, SetupTokenPurposes, ct);
        if (accessToken is null || accessToken.UserId != user.Id)
        {
            return AuthError(401, "TOKEN_INVALID", "The setup link is invalid or has expired.");
        }

        if (securityStatus is not ("invited" or "reset_pending" or "password_reset_required"))
        {
            return AuthError(409, "SETUP_NOT_PENDING", "This account does not have a pending setup request.");
        }

        var resolvedFullName = string.IsNullOrWhiteSpace(request.FullName)
            ? user.FullName
            : request.FullName.Trim();
        if (securityStatus == "invited" && string.IsNullOrWhiteSpace(resolvedFullName))
        {
            return AuthError(400, "INVALID_INVITE_PAYLOAD", "A full name is required to finish account setup.");
        }

        user.FullName = resolvedFullName;
        user.PasswordHash = PasswordHasher.Hash(request.Password.Trim());
        user.SecurityJson = UserSecurityProfile.SetStatus(user.SecurityJson, "active");
        user.UpdatedAtUtc = DateTime.UtcNow;

        accessToken.ConsumedAtUtc = DateTime.UtcNow;
        await InvalidateAccessTokensAsync(user.Id, SetupTokenPurposes, "superseded", ct, exceptTokenId: accessToken.Id);
        await RevokeSessionsAsync(user.Id, "setup_completed", ct);

        var authSession = await IssueAuthResponseAsync(user, null, ct);
        await _db.WriteAuditLogAsync(
            user.Id,
            user.Role,
            "auth.invite_completed",
            "user",
            user.Id,
            null,
            JsonSerializer.Serialize(new { user.Email, sessionId = authSession.Session.Id, previousStatus = securityStatus }),
            ct);

        return Ok(authSession.Response);
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-recovery")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email) || !email.Contains("@"))
        {
            return AuthError(400, "INVALID_EMAIL", "A valid email address is required.");
        }

        var user = await _db.Users.FirstOrDefaultAsync(x => x.Email == email, ct);
        if (user is null)
        {
            return Ok(new { ok = true, delivery = "silent", message = "If the account exists, reset instructions will be sent." });
        }

        var securityStatus = UserSecurityProfile.GetStatus(user.SecurityJson);
        if (securityStatus is "disabled" or "locked")
        {
            return Ok(new { ok = true, delivery = "silent", message = "If the account exists, reset instructions will be sent." });
        }

        var role = await _db.RoleDefinitions.FirstOrDefaultAsync(x => x.Name == user.Role, ct);
        if (role is null || !role.IsActive)
        {
            return Ok(new { ok = true, delivery = "silent", message = "If the account exists, reset instructions will be sent." });
        }

        var resetToken = AccessTokenCodec.GenerateToken();
        var resetExpiresAtUtc = DateTime.UtcNow.AddHours(4);
        user.SecurityJson = UserSecurityProfile.SetStatus(user.SecurityJson, "password_reset_required", "self_service_reset");
        user.UpdatedAtUtc = DateTime.UtcNow;
        await InvalidateAccessTokensAsync(user.Id, SetupTokenPurposes, "superseded", ct);
        await RevokeSessionsAsync(user.Id, "password_reset_requested", ct);

        var accessToken = new UserAccessToken
        {
            Id = $"uat_{Guid.NewGuid():N}",
            UserId = user.Id,
            Purpose = "password_reset",
            TokenHash = AccessTokenCodec.HashToken(resetToken),
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = resetExpiresAtUtc
        };
        var setupUrl = _accessLinkBuilder.BuildPasswordResetUrl(user.Email, resetToken);
        _db.UserAccessTokens.Add(accessToken);
        await _db.SaveChangesAsync(ct);

        var dispatch = await _accessEmailSender.SendPasswordResetAsync(user.Email, user.FullName, setupUrl, resetExpiresAtUtc, ct);
        await _db.WriteAuditLogAsync(
            user.Id,
            user.Role,
            "auth.password_reset_requested",
            "user",
            user.Id,
            null,
            JsonSerializer.Serialize(new { user.Email, resetExpiresAtUtc, dispatch.DeliveryMode }),
            ct);

        return Ok(new { ok = true, delivery = dispatch.DeliveryMode, message = "If the account exists, reset instructions will be sent." });
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return AuthError(400, "REFRESH_TOKEN_REQUIRED", "A refresh token is required.");
        }

        var refreshToken = await FindActiveAccessTokenAsync(request.RefreshToken, ["refresh"], ct);
        if (refreshToken is null || string.IsNullOrWhiteSpace(refreshToken.SessionId))
        {
            return AuthError(401, "REFRESH_TOKEN_INVALID", "The refresh token is invalid or expired.");
        }

        var session = await _db.UserSessions.FirstOrDefaultAsync(x => x.Id == refreshToken.SessionId, ct);
        if (session is null || session.RevokedAtUtc is not null)
        {
            return AuthError(401, "SESSION_EXPIRED", "The session has expired.");
        }

        var user = await _db.Users.FirstOrDefaultAsync(x => x.Id == refreshToken.UserId, ct);
        if (user is null)
        {
            return AuthError(401, "SESSION_EXPIRED", "The session has expired.");
        }

        var securityStatus = UserSecurityProfile.GetStatus(user.SecurityJson);
        if (securityStatus is not "active")
        {
            return AuthError(401, "SESSION_INACTIVE", "The account is no longer active.");
        }

        refreshToken.ConsumedAtUtc = DateTime.UtcNow;
        var authSession = await IssueAuthResponseAsync(user, session, ct);
        await _db.WriteAuditLogAsync(
            user.Id,
            user.Role,
            "auth.refreshed",
            "user_session",
            session.Id,
            null,
            JsonSerializer.Serialize(new { user.Email, sessionId = session.Id }),
            ct);

        return Ok(authSession.Response);
    }

    [HttpPost("change-password")]
    [Authorize]
    [EnableRateLimiting("auth-account")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken ct)
    {
        var userId = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var jwtId = User.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(jwtId))
        {
            return AuthError(401, "SESSION_EXPIRED", "Your session has expired.");
        }

        if (string.IsNullOrWhiteSpace(request.CurrentPassword))
        {
            return AuthError(400, "CURRENT_PASSWORD_REQUIRED", "Your current password is required.");
        }

        if (string.IsNullOrWhiteSpace(request.NextPassword) || request.NextPassword.Trim().Length < 8)
        {
            return AuthError(400, "PASSWORD_TOO_SHORT", "Password must be at least 8 characters long.");
        }

        if (request.CurrentPassword.Trim() == request.NextPassword.Trim())
        {
            return AuthError(400, "PASSWORD_REUSE", "Choose a new password that is different from the current one.");
        }

        var user = await _db.Users.FirstOrDefaultAsync(x => x.Id == userId, ct);
        if (user is null)
        {
            return AuthError(401, "SESSION_EXPIRED", "Your session has expired.");
        }

        if (!PasswordHasher.Verify(request.CurrentPassword, user.PasswordHash))
        {
            return AuthError(401, "CURRENT_PASSWORD_INVALID", "The current password is incorrect.");
        }

        var securityStatus = UserSecurityProfile.GetStatus(user.SecurityJson);
        if (securityStatus is not "active")
        {
            return AuthError(403, "ACCOUNT_DISABLED", "This account is disabled.");
        }

        var currentSession = await _db.UserSessions.FirstOrDefaultAsync(x => x.JwtId == jwtId, ct);
        if (currentSession is null || currentSession.RevokedAtUtc is not null)
        {
            return AuthError(401, "SESSION_EXPIRED", "Your session has expired.");
        }

        user.PasswordHash = PasswordHasher.Hash(request.NextPassword.Trim());
        user.UpdatedAtUtc = DateTime.UtcNow;

        await RevokeOtherSessionsAsync(user.Id, currentSession.Id, "password_changed", ct);
        await InvalidateRefreshTokensForSessionAsync(currentSession.Id, "rotated", ct);

        var authSession = await IssueAuthResponseAsync(user, currentSession, ct);
        await _db.WriteAuditLogAsync(
            user.Id,
            user.Role,
            "auth.password_changed",
            "user",
            user.Id,
            null,
            JsonSerializer.Serialize(new { user.Email, sessionId = currentSession.Id }),
            ct);

        return Ok(authSession.Response);
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var jwtId = User.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
        if (string.IsNullOrWhiteSpace(jwtId))
        {
            return NoContent();
        }

        var session = await _db.UserSessions.FirstOrDefaultAsync(x => x.JwtId == jwtId, ct);
        if (session is null || session.RevokedAtUtc is not null)
        {
            return NoContent();
        }

        session.RevokedAtUtc = DateTime.UtcNow;
        session.RevokedReason = "logout";
        await InvalidateRefreshTokensForSessionAsync(session.Id, "logout", ct);
        await _db.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(
            User,
            "auth.logout",
            "user_session",
            session.Id,
            null,
            JsonSerializer.Serialize(new { session.UserId, session.JwtId }),
            ct);

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
        var effectivePermissions = PermissionResolution.FilterClientVisiblePermissions(
                await PermissionResolution.ResolvePermissionsAsync(_db, role, user.Role, ct))
            .ToArray();

        return Ok(new
        {
            user = new
            {
                id = user.Id,
                fullName = user.FullName,
                email = user.Email,
                role = user.Role,
                roleScope = role?.Scope ?? RolePermissions.ScopeForRole(user.Role),
                permissions = effectivePermissions,
                clientIds = accessibleClientIds.OrderBy(x => x).ToArray()
            }
        });
    }

    private async Task<(object Response, UserSession Session)> IssueAuthResponseAsync(User user, UserSession? existingSession, CancellationToken ct)
    {
        var role = await _db.RoleDefinitions.FirstOrDefaultAsync(x => x.Name == user.Role, ct);
        if (role is null || !role.IsActive)
        {
            throw new InvalidOperationException("User role is inactive.");
        }

        var permissions = await PermissionResolution.ResolvePermissionsAsync(_db, role, user.Role, ct);
        var scope = RolePermissions.NormalizeScope(role.Scope);
        var jwtId = Guid.NewGuid().ToString("N");
        var expires = DateTime.UtcNow.AddMinutes(_jwtOptions.ExpiresMinutes);

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
                var clientIds = JsonSerializer.Deserialize<string[]>(user.ClientIdsJson)
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
                // Keep auth working even if legacy client scope JSON is malformed.
            }
        }
        else if (scope == "accountant" || scope == "admin")
        {
            var assignedClientIds = await _db.ClientAssignments
                .Where(x => x.AccountantUserId == user.Id)
                .Select(x => x.ClientId)
                .Distinct()
                .ToListAsync(ct);
            foreach (var clientId in assignedClientIds)
            {
                claims.Add(new Claim("assigned_client_id", clientId));
            }
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtOptions.SigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _jwtOptions.Issuer,
            audience: _jwtOptions.Audience,
            claims: claims,
            expires: expires,
            signingCredentials: creds
        );

        var session = existingSession ?? new UserSession
        {
            Id = $"sess_{Guid.NewGuid():N}",
            UserId = user.Id
        };

        session.JwtId = jwtId;
        session.IssuedAtUtc = DateTime.UtcNow;
        session.ExpiresAtUtc = expires;
        session.ClientIp = HttpContext.Connection.RemoteIpAddress?.ToString();
        session.UserAgent = Request.Headers.UserAgent.ToString();
        session.RevokedAtUtc = null;
        session.RevokedReason = null;

        if (existingSession is null)
        {
            _db.UserSessions.Add(session);
        }

        await InvalidateRefreshTokensForSessionAsync(session.Id, "rotated", ct);
        var refreshTokenValue = AccessTokenCodec.GenerateToken();
        var refreshToken = new UserAccessToken
        {
            Id = $"uat_{Guid.NewGuid():N}",
            UserId = user.Id,
            Purpose = "refresh",
            TokenHash = AccessTokenCodec.HashToken(refreshTokenValue),
            SessionId = session.Id,
            CreatedByUserId = user.Id,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(14)
        };
        _db.UserAccessTokens.Add(refreshToken);
        await _db.SaveChangesAsync(ct);

        return (
            new
            {
                token = new JwtSecurityTokenHandler().WriteToken(token),
                refreshToken = refreshTokenValue,
                expiresAtUtc = expires,
                refreshExpiresAtUtc = refreshToken.ExpiresAtUtc,
                user = new { user.Id, user.FullName, user.Email, user.Role, roleScope = scope },
                session = new { session.Id, session.ExpiresAtUtc }
            },
            session);
    }

    private async Task<UserAccessToken?> FindActiveAccessTokenAsync(string rawToken, IReadOnlyCollection<string> purposes, CancellationToken ct)
    {
        var tokenHash = AccessTokenCodec.HashToken(rawToken);
        var now = DateTime.UtcNow;
        return await _db.UserAccessTokens.FirstOrDefaultAsync(x =>
            x.TokenHash == tokenHash &&
            purposes.Contains(x.Purpose) &&
            x.ExpiresAtUtc > now &&
            x.ConsumedAtUtc == null &&
            x.InvalidatedAtUtc == null, ct);
    }

    private async Task InvalidateAccessTokensAsync(
        string userId,
        IReadOnlyCollection<string> purposes,
        string reason,
        CancellationToken ct,
        string? exceptTokenId = null)
    {
        var tokens = await _db.UserAccessTokens
            .Where(x => x.UserId == userId &&
                purposes.Contains(x.Purpose) &&
                x.InvalidatedAtUtc == null &&
                x.ConsumedAtUtc == null &&
                x.Id != exceptTokenId)
            .ToListAsync(ct);
        foreach (var token in tokens)
        {
            token.InvalidatedAtUtc = DateTime.UtcNow;
            token.InvalidatedReason = reason;
        }
    }

    private async Task InvalidateRefreshTokensForSessionAsync(string sessionId, string reason, CancellationToken ct)
    {
        var tokens = await _db.UserAccessTokens
            .Where(x => x.SessionId == sessionId &&
                x.Purpose == "refresh" &&
                x.InvalidatedAtUtc == null &&
                x.ConsumedAtUtc == null)
            .ToListAsync(ct);
        foreach (var token in tokens)
        {
            token.InvalidatedAtUtc = DateTime.UtcNow;
            token.InvalidatedReason = reason;
        }
    }

    private async Task RevokeSessionsAsync(string userId, string reason, CancellationToken ct)
    {
        var activeSessions = await _db.UserSessions
            .Where(x => x.UserId == userId && x.RevokedAtUtc == null && x.ExpiresAtUtc > DateTime.UtcNow)
            .ToListAsync(ct);
        foreach (var session in activeSessions)
        {
            session.RevokedAtUtc = DateTime.UtcNow;
            session.RevokedReason = reason;
            await InvalidateRefreshTokensForSessionAsync(session.Id, reason, ct);
        }
    }

    private async Task RevokeOtherSessionsAsync(string userId, string currentSessionId, string reason, CancellationToken ct)
    {
        var activeSessions = await _db.UserSessions
            .Where(x =>
                x.UserId == userId &&
                x.Id != currentSessionId &&
                x.RevokedAtUtc == null &&
                x.ExpiresAtUtc > DateTime.UtcNow)
            .ToListAsync(ct);
        foreach (var session in activeSessions)
        {
            session.RevokedAtUtc = DateTime.UtcNow;
            session.RevokedReason = reason;
            await InvalidateRefreshTokensForSessionAsync(session.Id, reason, ct);
        }
    }

    private ObjectResult AuthError(int statusCode, string code, string message)
    {
        return StatusCode(statusCode, new { code, message });
    }
}
