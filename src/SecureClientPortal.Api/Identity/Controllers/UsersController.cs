using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Models;
using System.Text.Json;

namespace SecureClientPortal.Backend.Controllers;

public record CreateUserRequest(
    string FullName,
    string Email,
    string Role,
    string? Password,
    string[]? ClientIds,
    string? Company);
public record UpdateUserActivationRequest(bool IsActive, string? Reason);

[ApiController]
[Route("api/users")]
[Authorize(Policy = "AdminOnly")]
public class UsersController : ControllerBase
{
    private readonly PortalDbContext _db;
    private readonly IAccessEmailSender _accessEmailSender;
    private readonly IAccessLinkBuilder _accessLinkBuilder;

    public UsersController(PortalDbContext db, IAccessEmailSender accessEmailSender, IAccessLinkBuilder accessLinkBuilder)
    {
        _db = db;
        _accessEmailSender = accessEmailSender;
        _accessLinkBuilder = accessLinkBuilder;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<object>>> GetAll()
    {
        var users = await _db.Users
            .OrderBy(x => x.FullName)
            .ToListAsync();
        var roles = (await _db.RoleDefinitions.ToListAsync())
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
                permissions = await PermissionResolution.ResolvePermissionsAsync(_db, role, user.Role),
                user.ProfileJson,
                user.SecurityJson,
                user.CreatedAtUtc,
                user.UpdatedAtUtc
            });
        }

        return Ok(payload);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest request, CancellationToken ct)
    {
        var normalizedRole = request.Role.Trim().ToLowerInvariant();
        var role = await _db.RoleDefinitions.FirstOrDefaultAsync(x => x.Name == normalizedRole, ct);
        if (role is null || !role.IsActive)
        {
            return BadRequest(new { error = "Role does not exist or is inactive." });
        }

        var email = request.Email.Trim().ToLowerInvariant();
        if (await _db.Users.AnyAsync(x => x.Email == email, ct))
        {
            return Conflict(new { error = "A user with this email already exists." });
        }

        var roleScope = RolePermissions.NormalizeScope(role.Scope);
        var clientIds = roleScope == "client"
            ? (request.ClientIds ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            : [];

        if (roleScope == "client" && clientIds.Length == 0)
        {
            return BadRequest(new { error = "Client users must be linked to at least one client." });
        }

        if (clientIds.Length > 0)
        {
            var knownClientIds = await _db.Clients
                .Where(x => clientIds.Contains(x.Id))
                .Select(x => x.Id)
                .ToListAsync(ct);
            if (knownClientIds.Count != clientIds.Length)
            {
                return BadRequest(new { error = "One or more client ids are invalid." });
            }
        }

        var inviteToken = AccessTokenCodec.GenerateToken();
        var inviteExpiresAtUtc = DateTime.UtcNow.AddDays(7);
        var user = new User
        {
            Id = $"u_{Guid.NewGuid():N}",
            FullName = request.FullName.Trim(),
            Email = email,
            Role = normalizedRole,
            PasswordHash = PasswordHasher.Hash(string.IsNullOrWhiteSpace(request.Password) ? "ChangeMe123!" : request.Password),
            ClientIdsJson = JsonSerializer.Serialize(clientIds),
            ProfileJson = string.IsNullOrWhiteSpace(request.Company)
                ? null
                : JsonSerializer.Serialize(new { company = request.Company.Trim() }),
            SecurityJson = UserSecurityProfile.SetStatus(null, "invited"),
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        var invite = new UserAccessToken
        {
            Id = $"uat_{Guid.NewGuid():N}",
            UserId = user.Id,
            Purpose = "invite",
            TokenHash = AccessTokenCodec.HashToken(inviteToken),
            CreatedByUserId = User.GetUserId(),
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = inviteExpiresAtUtc
        };
        var setupUrl = _accessLinkBuilder.BuildSetupUrl(user.Email, inviteToken);

        _db.Users.Add(user);
        _db.UserAccessTokens.Add(invite);
        await _db.SaveChangesAsync(ct);
        var dispatch = await _accessEmailSender.SendInviteAsync(user.Email, user.FullName, setupUrl, inviteExpiresAtUtc, ct);
        await _db.WriteAuditLogAsync(
            User,
            "users.created",
            "user",
            user.Id,
            clientIds.FirstOrDefault(),
            JsonSerializer.Serialize(new { user.Email, user.Role, clientIds, inviteExpiresAtUtc, dispatch.DeliveryMode }),
            ct);

        return Created($"/api/users/{user.Id}", new
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

    [HttpPost("{id}/activate")]
    public async Task<IActionResult> Activate(string id)
    {
        return await UpdateActivationAsync(id, true, null);
    }

    [HttpPost("{id}/deactivate")]
    public async Task<IActionResult> Deactivate(string id, [FromBody] UpdateUserActivationRequest? request = null)
    {
        return await UpdateActivationAsync(id, false, request?.Reason);
    }

    private async Task<IActionResult> UpdateActivationAsync(string id, bool isActive, string? reason)
    {
        var user = await _db.Users.FindAsync(id);
        if (user is null)
        {
            return NotFound();
        }

        user.SecurityJson = UserSecurityProfile.SetStatus(user.SecurityJson, isActive ? "active" : "disabled", reason);
        user.UpdatedAtUtc = DateTime.UtcNow;

        if (!isActive)
        {
            var activeSessions = await _db.UserSessions
                .Where(x => x.UserId == user.Id && x.RevokedAtUtc == null && x.ExpiresAtUtc > DateTime.UtcNow)
                .ToListAsync();
            foreach (var session in activeSessions)
            {
                session.RevokedAtUtc = DateTime.UtcNow;
                session.RevokedReason = "user_deactivated";
            }
        }

        await _db.SaveChangesAsync();
        await _db.WriteAuditLogAsync(
            User,
            isActive ? "users.activated" : "users.deactivated",
            "user",
            user.Id,
            null,
            JsonSerializer.Serialize(new { user.Email, isActive, reason }));

        return Ok(new { user.Id, isActive, securityStatus = UserSecurityProfile.GetStatus(user.SecurityJson) });
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
