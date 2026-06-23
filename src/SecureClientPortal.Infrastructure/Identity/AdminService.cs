using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts;
using SecureClientPortal.Backend.Application.Identity;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Models;
using System.Text.Json;

namespace SecureClientPortal.Backend.Infrastructure.Identity.Application;

public sealed class AdminService : IAdminService
{
    private readonly PortalDbContext _db;
    private readonly IAccessEmailSender _accessEmailSender;
    private readonly IAccessLinkBuilder _accessLinkBuilder;

    public AdminService(
        PortalDbContext db,
        IAccessEmailSender accessEmailSender,
        IAccessLinkBuilder accessLinkBuilder)
    {
        _db = db;
        _accessEmailSender = accessEmailSender;
        _accessLinkBuilder = accessLinkBuilder;
    }

    public async Task<IReadOnlyList<object>> GetUsersAsync(CancellationToken ct = default)
    {
        var users = await _db.Users.OrderBy(x => x.FullName).ToListAsync(ct);
        return users.Select(x => (object)new
        {
            x.Id,
            x.FullName,
            x.Email,
            x.Role,
            x.ProfileJson,
            x.SecurityJson,
            securityStatus = UserSecurityProfile.GetStatus(x.SecurityJson)
        }).ToList();
    }

    public async Task<ServiceResult<object>> CreateUserAsync(AdminCreateUserRequest request, System.Security.Claims.ClaimsPrincipal actor, CancellationToken ct = default)
    {
        IdentityValidators.ValidateAdminCreateUser(request);

        var email = request.Email.Trim().ToLowerInvariant();
        if (await _db.Users.AnyAsync(x => x.Email == email, ct))
        {
            return ServiceResult<object>.ErrorResult("A user with this email already exists.", statusCode: StatusCodes.Status409Conflict);
        }

        var roleName = request.Role.Trim().ToLowerInvariant();
        var role = await _db.RoleDefinitions.FirstOrDefaultAsync(x => x.Name == roleName && x.IsActive, ct);
        if (role is null)
        {
            return ServiceResult<object>.ErrorResult("Role does not exist or is inactive.");
        }

        var inviteToken = AccessTokenCodec.GenerateToken();
        var inviteExpiresAtUtc = DateTime.UtcNow.AddDays(7);
        var user = User.CreateInvited(
            Guid.NewGuid(),
            request.FullName,
            email,
            IdentityDomainValues.ToUserRole(roleName),
            PasswordHasher.Hash("ChangeMe123!"),
            "[]",
            string.IsNullOrWhiteSpace(request.Company) ? null : JsonSerializer.Serialize(new { company = request.Company.Trim() }));

        var invite = UserAccessToken.Create(
            Guid.NewGuid(),
            user.Id,
            "invite",
            AccessTokenCodec.HashToken(inviteToken),
            inviteExpiresAtUtc,
            null,
            actor.GetUserId());
        var setupUrl = _accessLinkBuilder.BuildSetupUrl(user.Email, inviteToken);

        _db.Users.Add(user);
        _db.UserAccessTokens.Add(invite);
        await _db.SaveChangesAsync(ct);

        AccessEmailDispatchResult dispatch;
        string? deliveryError = null;
        try
        {
            dispatch = await _accessEmailSender.SendInviteAsync(user.Email, user.FullName, setupUrl, inviteExpiresAtUtc, ct);
        }
        catch (Exception exception)
        {
            dispatch = new AccessEmailDispatchResult("failed", setupUrl);
            deliveryError = exception.Message;
        }

        await _db.WriteAuditLogAsync(
            actor,
            "users.created",
            "user",
            user.Id,
            null,
            JsonSerializer.Serialize(new { user.Email, user.Role, inviteExpiresAtUtc, dispatch.DeliveryMode, deliveryError }),
            ct);

        return ServiceResult<object>.Success(new
        {
            user.Id,
            user.FullName,
            user.Email,
            user.Role,
            invite = new
            {
                expiresAtUtc = inviteExpiresAtUtc,
                setupUrl
            },
            delivery = dispatch.DeliveryMode,
            deliveryError
        });
    }

    public async Task<ServiceResult<object>> UpdateUserRoleAsync(string id, AdminUpdateRoleRequest request, System.Security.Claims.ClaimsPrincipal actor, CancellationToken ct = default)
    {
        IdentityValidators.ValidateAdminUpdateRole(request);

        if (!Guid.TryParse(id, out var userId))
        {
            return ServiceResult<object>.NotFoundResult();
        }

        var user = await _db.Users.FindAsync([userId], ct);
        if (user is null)
        {
            return ServiceResult<object>.NotFoundResult();
        }

        var roleName = request.Role.Trim().ToLowerInvariant();
        var role = await _db.RoleDefinitions.FirstOrDefaultAsync(x => x.Name == roleName && x.IsActive, ct);
        if (role is null)
        {
            return ServiceResult<object>.ErrorResult("Role does not exist or is inactive.");
        }

        user.AssignRole(IdentityDomainValues.ToUserRole(roleName));
        await _db.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(
            actor,
            "users.role_changed",
            "user",
            user.Id,
            null,
            JsonSerializer.Serialize(new { user.Email, user.Role }),
            ct);

        return ServiceResult<object>.Success(new { user.Id, user.Role });
    }

    public async Task<ServiceResult<object>> UpdateUserStatusAsync(string id, AdminUpdateStatusRequest request, System.Security.Claims.ClaimsPrincipal actor, CancellationToken ct = default)
    {
        IdentityValidators.ValidateAdminUpdateStatus(request);

        if (!Guid.TryParse(id, out var userId))
        {
            return ServiceResult<object>.NotFoundResult();
        }

        var user = await _db.Users.FindAsync([userId], ct);
        if (user is null)
        {
            return ServiceResult<object>.NotFoundResult();
        }

        var normalizedStatus = request.Status.Trim().ToLowerInvariant();
        user.SetSecurityStatus(IdentityDomainValues.ToSecurityStatus(normalizedStatus));

        if (normalizedStatus is "disabled" or "locked")
        {
            var activeSessions = await _db.UserSessions
                .Where(x => x.UserId == user.Id && x.RevokedAtUtc == null && x.ExpiresAtUtc > DateTime.UtcNow)
                .ToListAsync(ct);
            foreach (var session in activeSessions)
            {
                session.Revoke($"status_{normalizedStatus}");
            }
        }

        await _db.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(
            actor,
            "users.status_changed",
            "user",
            user.Id,
            null,
            JsonSerializer.Serialize(new { user.Email, status = normalizedStatus }),
            ct);

        return ServiceResult<object>.Success(new { user.Id, status = normalizedStatus });
    }

    public async Task<ServiceResult<object>> ResetUserAccessAsync(string id, AdminResetAccessRequest request, System.Security.Claims.ClaimsPrincipal actor, CancellationToken ct = default)
    {
        IdentityValidators.ValidateAdminResetAccess(request);

        if (!Guid.TryParse(id, out var userId))
        {
            return ServiceResult<object>.NotFoundResult();
        }

        var user = await _db.Users.FindAsync([userId], ct);
        if (user is null)
        {
            return ServiceResult<object>.NotFoundResult();
        }

        var resetToken = AccessTokenCodec.GenerateToken();
        var resetExpiresAtUtc = DateTime.UtcNow.AddDays(7);
        user.SetSecurityStatus(SecurityStatus.PasswordResetRequired, request.Reason);

        var activeSessions = await _db.UserSessions
            .Where(x => x.UserId == user.Id && x.RevokedAtUtc == null && x.ExpiresAtUtc > DateTime.UtcNow)
            .ToListAsync(ct);
        foreach (var session in activeSessions)
        {
            session.Revoke("reset_access");
        }

        var existingTokens = await _db.UserAccessTokens
            .Where(x => x.UserId == user.Id && (x.Purpose == "invite" || x.Purpose == "password_reset") && x.InvalidatedAtUtc == null && x.ConsumedAtUtc == null)
            .ToListAsync(ct);
        foreach (var token in existingTokens)
        {
            token.Invalidate("superseded");
        }

        var resetAccessToken = UserAccessToken.Create(
            Guid.NewGuid(),
            user.Id,
            "password_reset",
            AccessTokenCodec.HashToken(resetToken),
            resetExpiresAtUtc,
            null,
            actor.GetUserId());
        var setupUrl = _accessLinkBuilder.BuildPasswordResetUrl(user.Email, resetToken);
        _db.UserAccessTokens.Add(resetAccessToken);
        await _db.SaveChangesAsync(ct);

        AccessEmailDispatchResult dispatch;
        string? deliveryError = null;
        try
        {
            dispatch = await _accessEmailSender.SendPasswordResetAsync(user.Email, user.FullName, setupUrl, resetExpiresAtUtc, ct);
        }
        catch (Exception exception)
        {
            dispatch = new AccessEmailDispatchResult("failed", setupUrl);
            deliveryError = exception.Message;
        }

        await _db.WriteAuditLogAsync(
            actor,
            "users.reset_access_requested",
            "user",
            user.Id,
            null,
            JsonSerializer.Serialize(new { user.Email, resetExpiresAtUtc, dispatch.DeliveryMode, deliveryError }),
            ct);

        return ServiceResult<object>.Success(new
        {
            user.Id,
            reset = true,
            invite = new
            {
                expiresAtUtc = resetExpiresAtUtc,
                setupUrl
            },
            delivery = dispatch.DeliveryMode,
            deliveryError
        });
    }

    public async Task<ServiceResult<object>> ResetPasswordAsync(string id, AdminResetPasswordRequest request, System.Security.Claims.ClaimsPrincipal actor, CancellationToken ct = default)
    {
        IdentityValidators.ValidateAdminResetPassword(request);

        if (!Guid.TryParse(id, out var userId))
        {
            return ServiceResult<object>.NotFoundResult();
        }

        var user = await _db.Users.FindAsync([userId], ct);
        if (user is null)
        {
            return ServiceResult<object>.NotFoundResult();
        }

        var temporaryPassword = string.IsNullOrWhiteSpace(request.NewPassword)
            ? $"Tmp!{Guid.NewGuid():N}"[..12]
            : request.NewPassword.Trim();

        user.SetPasswordHash(PasswordHasher.Hash(temporaryPassword));
        user.SetSecurityStatus(
            SecurityStatus.PasswordResetRequired,
            string.IsNullOrWhiteSpace(request.Reason) ? "admin_reset" : request.Reason.Trim());

        var activeSessions = await _db.UserSessions
            .Where(x => x.UserId == user.Id && x.RevokedAtUtc == null && x.ExpiresAtUtc > DateTime.UtcNow)
            .ToListAsync(ct);
        foreach (var session in activeSessions)
        {
            session.Revoke("password_reset");
        }

        await _db.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(
            actor,
            "users.password_reset",
            "user",
            user.Id,
            null,
            JsonSerializer.Serialize(new { user.Email }),
            ct);

        return ServiceResult<object>.Success(new { user.Id, temporaryPassword, reset = true });
    }

    public async Task<object> GetSettingAsync(string key, CancellationToken ct = default)
    {
        var item = await _db.SystemSettings.FindAsync([key], ct);
        return item is null
            ? new { key, valueJson = "{}" }
            : new { key = item.Key, valueJson = item.ValueJson };
    }

    public async Task<object> PutSettingAsync(string key, AdminSettingRequest request, CancellationToken ct = default)
    {
        IdentityValidators.ValidateAdminSetting(request);

        var item = await _db.SystemSettings.FindAsync([key], ct);
        if (item is null)
        {
            item = SystemSetting.Create(key, request.ValueJson);
            _db.SystemSettings.Add(item);
        }
        else
        {
            item.UpdateValue(request.ValueJson);
        }

        await _db.SaveChangesAsync(ct);
        return new { key = item.Key, valueJson = item.ValueJson };
    }
}
