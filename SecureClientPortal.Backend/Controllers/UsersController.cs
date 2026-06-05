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

    public UsersController(PortalDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<object>>> GetAll()
    {
        var users = await _db.Users
            .OrderBy(x => x.FullName)
            .ToListAsync();
        var roles = (await _db.RoleDefinitions.ToListAsync())
            .ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);

        var payload = users.Select(user =>
        {
            var clientIds = ParseClientIds(user.ClientIdsJson);
            roles.TryGetValue(user.Role, out var role);
            return new
            {
                user.Id,
                user.FullName,
                user.Email,
                user.Role,
                roleScope = role?.Scope ?? RolePermissions.ScopeForRole(user.Role),
                roleActive = role?.IsActive ?? true,
                clientIds,
                permissions = role is null
                    ? RolePermissions.ForRole(user.Role)
                    : RolePermissions.ParsePermissions(role.PermissionsJson, role.Name),
                user.ProfileJson,
                user.SecurityJson,
                user.CreatedAtUtc,
                user.UpdatedAtUtc
            };
        });

        return Ok(payload);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest request)
    {
        var normalizedRole = request.Role.Trim().ToLowerInvariant();
        var role = await _db.RoleDefinitions.FirstOrDefaultAsync(x => x.Name == normalizedRole);
        if (role is null || !role.IsActive)
        {
            return BadRequest(new { error = "Role does not exist or is inactive." });
        }

        var email = request.Email.Trim().ToLowerInvariant();
        if (await _db.Users.AnyAsync(x => x.Email == email))
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
                .ToListAsync();
            if (knownClientIds.Count != clientIds.Length)
            {
                return BadRequest(new { error = "One or more client ids are invalid." });
            }
        }

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

        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        await _db.WriteAuditLogAsync(
            User,
            "users.created",
            "user",
            user.Id,
            clientIds.FirstOrDefault(),
            JsonSerializer.Serialize(new { user.Email, user.Role, clientIds }));

        return Created($"/api/users/{user.Id}", new
        {
            user.Id,
            user.FullName,
            user.Email,
            user.Role,
            roleScope,
            clientIds,
            permissions = RolePermissions.ParsePermissions(role.PermissionsJson, role.Name)
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
