using SecureClientPortal.Backend.Data;
using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Identity;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Models;
using System.Security.Claims;
using System.Text.Json;
namespace SecureClientPortal.Backend.Infrastructure.Modules.UsersRoles.Application;
public sealed partial class AdminService
{
    public async Task<ServiceResult<object>> ResendInvitationAsync(string id, ClaimsPrincipal actor, CancellationToken ct = default)
    {
        if (!actor.HasPermission("users.create")) return ServiceResult<object>.ForbiddenResult();
        if (!Guid.TryParse(id, out var userId)) return ServiceResult<object>.NotFoundResult();
        await using var gate = await AccountAttemptLock.AcquireAsync(_db, "resend-invite:" + userId, ct);
        var user = await _db.Users.FindAsync([userId], ct);
        if (user is null) return ServiceResult<object>.NotFoundResult();
        if (UserSecurityProfile.GetStatus(user.SecurityJson) != "invited")
            return ServiceResult<object>.ErrorResult("Only accounts awaiting setup can receive another invitation.", statusCode: 409);
        var previous = await _db.UserAccessTokens.Where(x => x.UserId == userId && x.Purpose == "invite").ToListAsync(ct);
        if (previous.Any(x => x.CreatedAtUtc > DateTime.UtcNow.AddMinutes(-1)))
            return ServiceResult<object>.ErrorResult("Wait one minute before resending this invitation.", statusCode: 429);
        foreach (var token in previous.Where(x => x.ConsumedAtUtc == null)) token.Consume();
        var raw = AccessTokenCodec.GenerateToken(); var expiry = DateTime.UtcNow.AddHours(24);
        _db.UserAccessTokens.Add(UserAccessToken.Create(Guid.NewGuid(), userId, "invite", AccessTokenCodec.HashToken(raw), expiry, null, actor.GetUserId()));
        await _db.SaveChangesAsync(ct);
        string delivery; string? deliveryError = null;
        try { delivery = (await _accessEmailSender.SendInviteAsync(user.Email, user.FullName, _accessLinkBuilder.BuildSetupUrl(user.Email, raw), expiry, ct)).DeliveryMode; }
        catch (Exception) { delivery = "failed"; deliveryError = "Email delivery failed. Check the mail service and retry."; }
        await _db.WriteAuditLogAsync(actor, "users.invitation_resent", "user", userId, null, JsonSerializer.Serialize(new { delivery, expiresAtUtc = expiry }), ct);
        return ServiceResult<object>.Success(new { id = userId, delivery, deliveryError, expiresAtUtc = expiry });
    }
    public async Task<ServiceResult<object>> RevokeSessionsAsync(string id, ClaimsPrincipal actor, CancellationToken ct = default)
    {
        if (!actor.HasPermission("users.update")) return ServiceResult<object>.ForbiddenResult();
        if (!Guid.TryParse(id, out var userId) || !await _db.Users.AnyAsync(x => x.Id == userId, ct)) return ServiceResult<object>.NotFoundResult();
        var sessions = await _db.UserSessions.Where(x => x.UserId == userId && x.RevokedAtUtc == null).ToListAsync(ct);
        foreach (var session in sessions) session.Revoke("admin_session_revocation");
        var tokens = await _db.UserAccessTokens.Where(x => x.UserId == userId && x.Purpose == "refresh" && x.ConsumedAtUtc == null).ToListAsync(ct);
        foreach (var token in tokens) token.Consume();
        await _db.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(actor, "users.sessions_revoked", "user", userId, null, JsonSerializer.Serialize(new { count = sessions.Count }), ct);
        return ServiceResult<object>.Success(new { revokedSessions = sessions.Count });
    }
}
