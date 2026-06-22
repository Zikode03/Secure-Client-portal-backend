using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts;
using SecureClientPortal.Backend.Application.Identity;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace SecureClientPortal.Backend.Infrastructure.Identity.Application;

public sealed class AuthService : IAuthService
{
    private static readonly string[] SetupTokenPurposes = ["invite", "password_reset"];

    private readonly PortalDbContext _db;
    private readonly JwtOptions _jwtOptions;
    private readonly IAccessEmailSender _accessEmailSender;
    private readonly IAccessLinkBuilder _accessLinkBuilder;

    public AuthService(
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

    public async Task<ServiceResult<object>> LoginAsync(LoginRequest request, HttpContext httpContext, CancellationToken ct = default)
    {
        IdentityValidators.ValidateLogin(request);

        var email = request.Email.Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(x => x.Email == email, ct);
        if (user is null || !PasswordHasher.Verify(request.Password, user.PasswordHash))
        {
            return AuthFailure("INVALID_CREDENTIALS", "The email or password is incorrect.");
        }

        var securityStatus = UserSecurityProfile.GetStatus(user.SecurityJson);
        if (securityStatus is "invited" or "reset_pending")
        {
            return AuthFailure("ACCOUNT_SETUP_REQUIRED", "This account still needs to finish setup before it can sign in.");
        }

        if (securityStatus is "password_reset_required")
        {
            return AuthFailure("PASSWORD_RESET_REQUIRED", "This account needs a password reset before it can sign in.");
        }

        if (securityStatus is "disabled" or "locked")
        {
            return AuthFailure("ACCOUNT_DISABLED", "This account is disabled.");
        }

        user.UpdatedAtUtc = DateTime.UtcNow;
        var authSession = await IssueAuthResponseAsync(user, null, httpContext, ct);
        await _db.WriteAuditLogAsync(
            user.Id,
            user.Role,
            "auth.login",
            "user",
            user.Id,
            null,
            JsonSerializer.Serialize(new { user.Email, user.Role, sessionId = authSession.Session.Id }),
            ct);

        return ServiceResult<object>.Success(authSession.Response);
    }

    public async Task<ServiceResult<object>> CompleteInviteAsync(CompleteInviteRequest request, HttpContext httpContext, CancellationToken ct = default)
    {
        IdentityValidators.ValidateCompleteInvite(request);

        var email = request.Email.Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(x => x.Email == email, ct);
        if (user is null)
        {
            return AuthFailure("TOKEN_INVALID", "The setup link is invalid or has expired.");
        }

        var securityStatus = UserSecurityProfile.GetStatus(user.SecurityJson);
        if (securityStatus is "disabled" or "locked")
        {
            return ServiceResult<object>.ForbiddenResult("This account is disabled.", "ACCOUNT_DISABLED");
        }

        var accessToken = await FindActiveAccessTokenAsync(request.Token, SetupTokenPurposes, ct);
        if (accessToken is null || accessToken.UserId != user.Id)
        {
            return AuthFailure("TOKEN_INVALID", "The setup link is invalid or has expired.");
        }

        if (securityStatus is not ("invited" or "reset_pending" or "password_reset_required"))
        {
            return ServiceResult<object>.ErrorResult("This account does not have a pending setup request.", "SETUP_NOT_PENDING", StatusCodes.Status409Conflict);
        }

        var resolvedFullName = string.IsNullOrWhiteSpace(request.FullName)
            ? user.FullName
            : request.FullName.Trim();
        if (securityStatus == "invited" && string.IsNullOrWhiteSpace(resolvedFullName))
        {
            return ServiceResult<object>.ErrorResult("A full name is required to finish account setup.", "INVALID_INVITE_PAYLOAD");
        }

        user.FullName = resolvedFullName;
        user.PasswordHash = PasswordHasher.Hash(request.Password.Trim());
        user.SecurityJson = UserSecurityProfile.SetStatus(user.SecurityJson, "active");
        user.UpdatedAtUtc = DateTime.UtcNow;

        accessToken.ConsumedAtUtc = DateTime.UtcNow;
        await InvalidateAccessTokensAsync(user.Id, SetupTokenPurposes, "superseded", ct, exceptTokenId: accessToken.Id);
        await RevokeSessionsAsync(user.Id, "setup_completed", ct);

        var authSession = await IssueAuthResponseAsync(user, null, httpContext, ct);
        await _db.WriteAuditLogAsync(
            user.Id,
            user.Role,
            "auth.invite_completed",
            "user",
            user.Id,
            null,
            JsonSerializer.Serialize(new { user.Email, sessionId = authSession.Session.Id, previousStatus = securityStatus }),
            ct);

        return ServiceResult<object>.Success(authSession.Response);
    }

    public async Task<ServiceResult<object>> ForgotPasswordAsync(ForgotPasswordRequest request, HttpContext httpContext, CancellationToken ct = default)
    {
        IdentityValidators.ValidateForgotPassword(request);

        var email = request.Email.Trim().ToLowerInvariant();
        var silentSuccess = new { ok = true, delivery = "silent", message = "If the account exists, reset instructions will be sent." };

        var user = await _db.Users.FirstOrDefaultAsync(x => x.Email == email, ct);
        if (user is null)
        {
            return ServiceResult<object>.Success(silentSuccess);
        }

        var securityStatus = UserSecurityProfile.GetStatus(user.SecurityJson);
        if (securityStatus is "disabled" or "locked")
        {
            return ServiceResult<object>.Success(silentSuccess);
        }

        var role = await _db.RoleDefinitions.FirstOrDefaultAsync(x => x.Name == user.Role, ct);
        if (role is null || !role.IsActive)
        {
            return ServiceResult<object>.Success(silentSuccess);
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

        return ServiceResult<object>.Success(new { ok = true, delivery = dispatch.DeliveryMode, message = "If the account exists, reset instructions will be sent." });
    }

    public async Task<ServiceResult<object>> RefreshAsync(RefreshTokenRequest request, HttpContext httpContext, CancellationToken ct = default)
    {
        IdentityValidators.ValidateRefresh(request);

        var refreshToken = await FindActiveAccessTokenAsync(request.RefreshToken, ["refresh"], ct);
        if (refreshToken is null || string.IsNullOrWhiteSpace(refreshToken.SessionId))
        {
            return AuthFailure("REFRESH_TOKEN_INVALID", "The refresh token is invalid or expired.");
        }

        var session = await _db.UserSessions.FirstOrDefaultAsync(x => x.Id == refreshToken.SessionId, ct);
        if (session is null || session.RevokedAtUtc is not null)
        {
            return AuthFailure("SESSION_EXPIRED", "The session has expired.");
        }

        var user = await _db.Users.FirstOrDefaultAsync(x => x.Id == refreshToken.UserId, ct);
        if (user is null)
        {
            return AuthFailure("SESSION_EXPIRED", "The session has expired.");
        }

        var securityStatus = UserSecurityProfile.GetStatus(user.SecurityJson);
        if (securityStatus is not "active")
        {
            return AuthFailure("SESSION_INACTIVE", "The account is no longer active.");
        }

        refreshToken.ConsumedAtUtc = DateTime.UtcNow;
        var authSession = await IssueAuthResponseAsync(user, session, httpContext, ct);
        await _db.WriteAuditLogAsync(
            user.Id,
            user.Role,
            "auth.refreshed",
            "user_session",
            session.Id,
            null,
            JsonSerializer.Serialize(new { user.Email, sessionId = session.Id }),
            ct);

        return ServiceResult<object>.Success(authSession.Response);
    }

    public async Task<ServiceResult<object>> ChangePasswordAsync(ChangePasswordRequest request, System.Security.Claims.ClaimsPrincipal actor, HttpContext httpContext, CancellationToken ct = default)
    {
        IdentityValidators.ValidateChangePassword(request);

        var userId = actor.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? actor.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var jwtId = actor.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(jwtId))
        {
            return AuthFailure("SESSION_EXPIRED", "Your session has expired.");
        }

        var user = await _db.Users.FirstOrDefaultAsync(x => x.Id == userId, ct);
        if (user is null)
        {
            return AuthFailure("SESSION_EXPIRED", "Your session has expired.");
        }

        if (!PasswordHasher.Verify(request.CurrentPassword, user.PasswordHash))
        {
            return AuthFailure("CURRENT_PASSWORD_INVALID", "The current password is incorrect.");
        }

        var securityStatus = UserSecurityProfile.GetStatus(user.SecurityJson);
        if (securityStatus is not "active")
        {
            return ServiceResult<object>.ForbiddenResult("This account is disabled.", "ACCOUNT_DISABLED");
        }

        var currentSession = await _db.UserSessions.FirstOrDefaultAsync(x => x.JwtId == jwtId, ct);
        if (currentSession is null || currentSession.RevokedAtUtc is not null)
        {
            return AuthFailure("SESSION_EXPIRED", "Your session has expired.");
        }

        user.PasswordHash = PasswordHasher.Hash(request.NextPassword.Trim());
        user.UpdatedAtUtc = DateTime.UtcNow;

        await RevokeOtherSessionsAsync(user.Id, currentSession.Id, "password_changed", ct);
        await InvalidateRefreshTokensForSessionAsync(currentSession.Id, "rotated", ct);

        var authSession = await IssueAuthResponseAsync(user, currentSession, httpContext, ct);
        await _db.WriteAuditLogAsync(
            user.Id,
            user.Role,
            "auth.password_changed",
            "user",
            user.Id,
            null,
            JsonSerializer.Serialize(new { user.Email, sessionId = currentSession.Id }),
            ct);

        return ServiceResult<object>.Success(authSession.Response);
    }

    public async Task LogoutAsync(System.Security.Claims.ClaimsPrincipal actor, CancellationToken ct = default)
    {
        var jwtId = actor.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
        if (string.IsNullOrWhiteSpace(jwtId))
        {
            return;
        }

        var session = await _db.UserSessions.FirstOrDefaultAsync(x => x.JwtId == jwtId, ct);
        if (session is null || session.RevokedAtUtc is not null)
        {
            return;
        }

        session.RevokedAtUtc = DateTime.UtcNow;
        session.RevokedReason = "logout";
        await InvalidateRefreshTokensForSessionAsync(session.Id, "logout", ct);
        await _db.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(
            actor,
            "auth.logout",
            "user_session",
            session.Id,
            null,
            JsonSerializer.Serialize(new { session.UserId, session.JwtId }),
            ct);
    }

    public async Task<ServiceResult<object>> MeAsync(System.Security.Claims.ClaimsPrincipal actor, CancellationToken ct = default)
    {
        var userId = actor.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? actor.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return ServiceResult<object>.UnauthorizedResult();
        }

        var user = await _db.Users.FirstOrDefaultAsync(x => x.Id == userId, ct);
        if (user is null)
        {
            return ServiceResult<object>.NotFoundResult();
        }

        var role = await _db.RoleDefinitions.FirstOrDefaultAsync(x => x.Name == user.Role, ct);
        var accessibleClientIds = await actor.GetAccessibleClientIdsAsync(_db, ct);
        var effectivePermissions = PermissionResolution.FilterClientVisiblePermissions(
                await PermissionResolution.ResolvePermissionsAsync(_db, role, user.Role, ct))
            .ToArray();

        return ServiceResult<object>.Success(new
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

    private async Task<(object Response, UserSession Session)> IssueAuthResponseAsync(User user, UserSession? existingSession, HttpContext httpContext, CancellationToken ct)
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
            }
        }
        else if (scope is "accountant" or "admin")
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
            signingCredentials: creds);

        var session = existingSession ?? new UserSession
        {
            Id = $"sess_{Guid.NewGuid():N}",
            UserId = user.Id
        };

        session.JwtId = jwtId;
        session.IssuedAtUtc = DateTime.UtcNow;
        session.ExpiresAtUtc = expires;
        session.ClientIp = httpContext.Connection.RemoteIpAddress?.ToString();
        session.UserAgent = httpContext.Request.Headers.UserAgent.ToString();
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

    private async Task InvalidateAccessTokensAsync(string userId, IReadOnlyCollection<string> purposes, string reason, CancellationToken ct, string? exceptTokenId = null)
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

    private static ServiceResult<object> AuthFailure(string code, string message)
    {
        return ServiceResult<object>.UnauthorizedResult(message, code);
    }
}
