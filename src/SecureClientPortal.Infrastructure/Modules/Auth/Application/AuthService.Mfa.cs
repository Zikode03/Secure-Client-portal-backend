using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts.Modules.Auth;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Models;
using System.Security.Cryptography;
using System.Text.Json;

namespace SecureClientPortal.Backend.Infrastructure.Modules.Auth.Application;

public sealed partial class AuthService
{
    private static readonly string DummyPasswordHash = PasswordHasher.Hash(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
    private async Task<AccountSecurity> SecurityAsync(Guid id, CancellationToken ct)
    {
        var security = await _db.AccountSecurities.FindAsync([id], ct);
        if (security is null) { security = new AccountSecurity { Id = id }; _db.AccountSecurities.Add(security); }
        return security;
    }
    private async Task<ServiceResult<object>> BeginMfaAsync(User user, bool persistent, CancellationToken ct)
    {
        var security = await SecurityAsync(user.Id, ct);
        var role = await _db.RoleDefinitions.FirstOrDefaultAsync(x => x.Name == user.Role && x.IsActive, ct);
        if (role is null) return AuthFailure("ACCOUNT_DISABLED", "This account is unavailable.");
        var challenge = user.Id.ToString("N") + "." + AccessTokenCodec.GenerateToken();
        security.ChallengeHash = AccessTokenCodec.HashToken(challenge);
        security.ChallengeExpiresUtc = DateTime.UtcNow.AddMinutes(5);
        security.Persistent = persistent;
        security.PendingSecret = security.MfaSecret is null ? ProtectMfa(Totp.NewSecret()) : null;
        security.Version = Guid.NewGuid();
        await _db.SaveChangesAsync(ct);
        return ServiceResult<object>.Success(new {
            mfaRequired = true, challengeToken = challenge,
            setupKey = security.PendingSecret is null ? null : UnprotectMfa(security.PendingSecret),
            expiresAtUtc = security.ChallengeExpiresUtc
        });
    }
    private string ProtectMfa(string secret) => (_dataProtection ?? throw new InvalidOperationException("MFA protection unavailable."))
        .CreateProtector("SecureClientPortal.Mfa.v1").Protect(secret);
    private string UnprotectMfa(string secret) => (_dataProtection ?? throw new InvalidOperationException("MFA protection unavailable."))
        .CreateProtector("SecureClientPortal.Mfa.v1").Unprotect(secret);

    public async Task<ServiceResult<object>> VerifyMfaAsync(MfaVerifyRequest request, HttpContext context, CancellationToken ct = default)
    {
        if (request.ChallengeToken?.Length > 256 || request.Code?.Length > 128 ||
            !Guid.TryParse(request.ChallengeToken?.Split('.')[0], out var id))
            return AuthFailure("MFA_INVALID", "The verification session is invalid or expired.");
        var user = await _db.Users.FindAsync([id], ct);
        if (user is null) return AuthFailure("MFA_INVALID", "The verification session is invalid or expired.");
        await using var gate = await AccountAttemptLock.AcquireAsync(_db, user.Email, ct);
        await _db.Entry(user).ReloadAsync(ct);
        var security = await SecurityAsync(id, ct);
        if (security.LockedUntilUtc > DateTime.UtcNow)
            return ServiceResult<object>.ErrorResult("Too many attempts. Try again in 15 minutes.", "ACCOUNT_THROTTLED", 429);
        if (UserSecurityProfile.GetStatus(user.SecurityJson) != "active" ||
            security.ChallengeExpiresUtc is null || security.ChallengeExpiresUtc <= DateTime.UtcNow ||
            security.ChallengeHash != AccessTokenCodec.HashToken(request.ChallengeToken!))
            return AuthFailure("MFA_INVALID", "The verification session is invalid or expired.");

        var code = (request.Code ?? "").Trim();
        // Recovery requires the password challenge AND a saved one-time code; email alone cannot remove MFA.
        if (request.UseRecoveryCode && security.MfaSecret is not null && security.PendingSecret is null)
        {
            var hashes = JsonSerializer.Deserialize<List<string>>(security.RecoveryHashesJson) ?? [];
            if (hashes.Remove(AccessTokenCodec.HashToken(code.ToUpperInvariant())))
            {
                security.RecoveryHashesJson = JsonSerializer.Serialize(hashes);
                security.PendingSecret = ProtectMfa(Totp.NewSecret());
                security.ChallengeExpiresUtc = DateTime.UtcNow.AddMinutes(5);
                security.Version = Guid.NewGuid();
                await RevokeSessionsAsync(id, "mfa_recovery", ct);
                await _db.SaveChangesAsync(ct);
                return ServiceResult<object>.Success(new { mfaRequired = true, challengeToken = request.ChallengeToken,
                    setupKey = UnprotectMfa(security.PendingSecret), expiresAtUtc = security.ChallengeExpiresUtc });
            }
        }
        var encryptedSecret = security.PendingSecret ?? security.MfaSecret;
        var step = !request.UseRecoveryCode && encryptedSecret is not null
            ? Totp.Verify(UnprotectMfa(encryptedSecret), code, security.PendingSecret is null ? security.LastTotpStep : -1, DateTimeOffset.UtcNow)
            : null;
        if (step is null)
        {
            security.Fail(DateTime.UtcNow);
            await _db.SaveChangesAsync(ct);
            return AuthFailure("MFA_INVALID", "The code is incorrect, expired, or already used.");
        }
        string[]? recoveryCodes = null;
        if (security.PendingSecret is not null)
        {
            security.MfaSecret = security.PendingSecret;
            security.PendingSecret = null;
            recoveryCodes = Enumerable.Range(0, 10).Select(_ => Convert.ToHexString(RandomNumberGenerator.GetBytes(16))).ToArray();
            security.RecoveryHashesJson = JsonSerializer.Serialize(recoveryCodes.Select(AccessTokenCodec.HashToken));
            await RevokeSessionsAsync(id, "mfa_enrolled", ct);
        }
        security.LastTotpStep = step.Value;
        security.ChallengeHash = null;
        security.ChallengeExpiresUtc = null;
        security.Succeed();
        context.Items["mfa_verified"] = true;
        var session = await IssueAuthResponseAsync(user, null, context, security.Persistent, ct);
        await _db.WriteAuditLogAsync(user.Id, user.Role, recoveryCodes is null ? "auth.mfa_login" : "auth.mfa_enrolled",
            "user", user.Id, null, null, ct);
        return ServiceResult<object>.Success(new { authenticated = true, recoveryCodes });
    }
}
