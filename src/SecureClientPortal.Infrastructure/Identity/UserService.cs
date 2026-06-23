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

public sealed class UserService : IUserService
{
    private readonly PortalDbContext _db;
    private readonly IAccessEmailSender _accessEmailSender;
    private readonly IAccessLinkBuilder _accessLinkBuilder;

    public UserService(
        PortalDbContext db,
        IAccessEmailSender accessEmailSender,
        IAccessLinkBuilder accessLinkBuilder)
    {
        _db = db;
        _accessEmailSender = accessEmailSender;
        _accessLinkBuilder = accessLinkBuilder;
    }

    public async Task<IReadOnlyList<object>> GetAllAsync(CancellationToken ct = default)
    {
        var users = await _db.Users
            .OrderBy(x => x.FullName)
            .ToListAsync(ct);
        var roles = (await _db.RoleDefinitions.ToListAsync(ct))
            .ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);

        var payload = new List<object>(users.Count);
        foreach (var user in users)
        {
            var clientIds = ParseClientIds(user.ClientIdsJson);
            roles.TryGetValue(user.Role, out var role);
            payload.Add(new
            {
                user.Id,
                user.FullName,
                user.Email,
                user.Role,
                roleScope = role?.Scope ?? RolePermissions.ScopeForRole(user.Role),
                roleActive = role?.IsActive ?? true,
                clientIds,
                permissions = await PermissionResolution.ResolvePermissionsAsync(_db, role, user.Role, ct),
                user.ProfileJson,
                user.SecurityJson,
                user.CreatedAtUtc,
                user.UpdatedAtUtc
            });
        }

        return payload;
    }

    public async Task<ServiceResult<object>> CreateAsync(CreateUserRequest request, System.Security.Claims.ClaimsPrincipal actor, CancellationToken ct = default)
    {
        IdentityValidators.ValidateCreateUser(request);

        var normalizedRole = request.Role.Trim().ToLowerInvariant();
        var role = await _db.RoleDefinitions.FirstOrDefaultAsync(x => x.Name == normalizedRole, ct);
        if (role is null || !role.IsActive)
        {
            return ServiceResult<object>.ErrorResult("Role does not exist or is inactive.");
        }

        var email = request.Email.Trim().ToLowerInvariant();
        if (await _db.Users.AnyAsync(x => x.Email == email, ct))
        {
            return ServiceResult<object>.ErrorResult("A user with this email already exists.", statusCode: StatusCodes.Status409Conflict);
        }

        var roleScope = RolePermissions.NormalizeScope(role.Scope);
        var clientIds = roleScope == "client"
            ? (request.ClientIds ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            : [];

        if (roleScope == "client" && clientIds.Length == 0)
        {
            return ServiceResult<object>.ErrorResult("Client users must be linked to at least one client.");
        }

        if (clientIds.Length > 0)
        {
            var knownClientIds = await _db.Clients
                .Where(x => clientIds.Contains(x.Id))
                .Select(x => x.Id)
                .ToListAsync(ct);
            if (knownClientIds.Count != clientIds.Length)
            {
                return ServiceResult<object>.ErrorResult("One or more client ids are invalid.");
            }
        }

        var inviteToken = AccessTokenCodec.GenerateToken();
        var inviteExpiresAtUtc = DateTime.UtcNow.AddDays(7);
        var user = User.CreateInvited(
            $"u_{Guid.NewGuid():N}",
            request.FullName,
            email,
            IdentityDomainValues.ToUserRole(normalizedRole),
            PasswordHasher.Hash(string.IsNullOrWhiteSpace(request.Password) ? "ChangeMe123!" : request.Password),
            JsonSerializer.Serialize(clientIds),
            string.IsNullOrWhiteSpace(request.Company)
                ? null
                : JsonSerializer.Serialize(new { company = request.Company.Trim() }));

        var invite = UserAccessToken.Create(
            $"uat_{Guid.NewGuid():N}",
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

        var dispatch = await _accessEmailSender.SendInviteAsync(user.Email, user.FullName, setupUrl, inviteExpiresAtUtc, ct);
        await _db.WriteAuditLogAsync(
            actor,
            "users.created",
            "user",
            user.Id,
            clientIds.FirstOrDefault(),
            JsonSerializer.Serialize(new { user.Email, user.Role, clientIds, inviteExpiresAtUtc, dispatch.DeliveryMode }),
            ct);

        return ServiceResult<object>.Success(new
        {
            user.Id,
            user.FullName,
            user.Email,
            user.Role,
            roleScope,
            clientIds,
            permissions = await PermissionResolution.ResolvePermissionsAsync(_db, role, user.Role, ct),
            invite = new
            {
                expiresAtUtc = inviteExpiresAtUtc,
                setupUrl
            },
            delivery = dispatch.DeliveryMode
        });
    }

    public async Task<ServiceResult<object>> UpdateActivationAsync(string id, UpdateUserActivationRequest request, System.Security.Claims.ClaimsPrincipal actor, CancellationToken ct = default)
    {
        IdentityValidators.ValidateUpdateUserActivation(request);

        var user = await _db.Users.FindAsync([id], ct);
        if (user is null)
        {
            return ServiceResult<object>.NotFoundResult();
        }

        user.SetSecurityStatus(request.IsActive ? SecurityStatus.Active : SecurityStatus.Disabled, request.Reason);

        if (!request.IsActive)
        {
            var activeSessions = await _db.UserSessions
                .Where(x => x.UserId == user.Id && x.RevokedAtUtc == null && x.ExpiresAtUtc > DateTime.UtcNow)
                .ToListAsync(ct);
            foreach (var session in activeSessions)
            {
                session.Revoke("user_deactivated");
            }
        }

        await _db.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(
            actor,
            request.IsActive ? "users.activated" : "users.deactivated",
            "user",
            user.Id,
            null,
            JsonSerializer.Serialize(new { user.Email, request.IsActive, request.Reason }),
            ct);

        return ServiceResult<object>.Success(new { user.Id, isActive = request.IsActive, securityStatus = UserSecurityProfile.GetStatus(user.SecurityJson) });
    }

    private static string[] ParseClientIds(string rawJson)
    {
        try
        {
            return JsonSerializer.Deserialize<string[]>(rawJson)?
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? [];
        }
        catch
        {
            return [];
        }
    }
}

