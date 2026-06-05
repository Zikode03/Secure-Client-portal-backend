using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Models;
using System.Text.Json;

namespace SecureClientPortal.Backend.Controllers;

public record AdminCreateUserRequest(string FullName, string Email, string Role, string? Company);
public record AdminUpdateRoleRequest(string Role);
public record AdminUpdateStatusRequest(string Status);
public record AdminResetAccessRequest(string Reason);
public record AdminResetPasswordRequest(string? NewPassword, string? Reason);
public record AdminSettingRequest(string ValueJson);

[ApiController]
[Route("api/admin")]
[Authorize(Policy = "AdminOnly")]
public class AdminController : ControllerBase
{
    private readonly PortalDbContext _db;

    public AdminController(PortalDbContext db)
    {
        _db = db;
    }

    [HttpGet("users")]
    public async Task<ActionResult<IEnumerable<object>>> GetUsers()
    {
        var users = await _db.Users
            .OrderBy(x => x.FullName)
            .Select(x => new { x.Id, x.FullName, x.Email, x.Role, x.ProfileJson, x.SecurityJson })
            .ToListAsync();
        return Ok(users);
    }

    [HttpPost("users")]
    public async Task<IActionResult> CreateUser([FromBody] AdminCreateUserRequest request)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        if (await _db.Users.AnyAsync(x => x.Email == email))
        {
            return Conflict(new { error = "A user with this email already exists." });
        }
        var roleName = request.Role.Trim().ToLowerInvariant();
        var role = await _db.RoleDefinitions.FirstOrDefaultAsync(x => x.Name == roleName && x.IsActive);
        if (role is null)
        {
            return BadRequest(new { error = "Role does not exist or is inactive." });
        }

        var user = new User
        {
            Id = $"u_{Guid.NewGuid():N}",
            FullName = request.FullName.Trim(),
            Email = email,
            Role = roleName,
            PasswordHash = Auth.PasswordHasher.Hash("ChangeMe123!"),
            ClientIdsJson = "[]",
            ProfileJson = string.IsNullOrWhiteSpace(request.Company) ? null : $"{{\"company\":\"{request.Company}\"}}",
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
            null,
            JsonSerializer.Serialize(new { user.Email, user.Role }));
        return Ok(new { user.Id, user.FullName, user.Email, user.Role });
    }

    [HttpPut("users/{id}/role")]
    public async Task<IActionResult> UpdateUserRole(string id, [FromBody] AdminUpdateRoleRequest request)
    {
        var user = await _db.Users.FindAsync(id);
        if (user is null) return NotFound();
        var roleName = request.Role.Trim().ToLowerInvariant();
        var role = await _db.RoleDefinitions.FirstOrDefaultAsync(x => x.Name == roleName && x.IsActive);
        if (role is null)
        {
            return BadRequest(new { error = "Role does not exist or is inactive." });
        }

        user.Role = roleName;
        user.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _db.WriteAuditLogAsync(
            User,
            "users.role_changed",
            "user",
            user.Id,
            null,
            JsonSerializer.Serialize(new { user.Email, user.Role }));
        return Ok(new { user.Id, user.Role });
    }

    [HttpPut("users/{id}/status")]
    public async Task<IActionResult> UpdateUserStatus(string id, [FromBody] AdminUpdateStatusRequest request)
    {
        var user = await _db.Users.FindAsync(id);
        if (user is null) return NotFound();
        var normalizedStatus = request.Status.Trim().ToLowerInvariant();
        user.SecurityJson = UserSecurityProfile.SetStatus(user.SecurityJson, normalizedStatus);
        user.UpdatedAtUtc = DateTime.UtcNow;

        if (normalizedStatus is "disabled" or "locked")
        {
            var activeSessions = await _db.UserSessions
                .Where(x => x.UserId == user.Id && x.RevokedAtUtc == null && x.ExpiresAtUtc > DateTime.UtcNow)
                .ToListAsync();
            foreach (var session in activeSessions)
            {
                session.RevokedAtUtc = DateTime.UtcNow;
                session.RevokedReason = $"status_{normalizedStatus}";
            }
        }

        await _db.SaveChangesAsync();
        await _db.WriteAuditLogAsync(
            User,
            "users.status_changed",
            "user",
            user.Id,
            null,
            JsonSerializer.Serialize(new { user.Email, status = normalizedStatus }));
        return Ok(new { user.Id, status = normalizedStatus });
    }

    [HttpPost("users/{id}/disable")]
    public async Task<IActionResult> DisableUser(string id)
    {
        return await UpdateUserStatus(id, new AdminUpdateStatusRequest("disabled"));
    }

    [HttpPost("users/{id}/enable")]
    public async Task<IActionResult> EnableUser(string id)
    {
        return await UpdateUserStatus(id, new AdminUpdateStatusRequest("active"));
    }

    [HttpPost("users/{id}/reset-access")]
    public async Task<IActionResult> ResetUserAccess(string id, [FromBody] AdminResetAccessRequest request)
    {
        var user = await _db.Users.FindAsync(id);
        if (user is null) return NotFound();
        user.SecurityJson = UserSecurityProfile.SetStatus(user.SecurityJson, "reset_pending", request.Reason);
        user.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(new { user.Id, reset = true });
    }

    [HttpPost("users/{id}/reset-password")]
    public async Task<IActionResult> ResetPassword(string id, [FromBody] AdminResetPasswordRequest request)
    {
        var user = await _db.Users.FindAsync(id);
        if (user is null) return NotFound();

        var temporaryPassword = string.IsNullOrWhiteSpace(request.NewPassword)
            ? $"Tmp!{Guid.NewGuid():N}"[..12]
            : request.NewPassword.Trim();

        user.PasswordHash = Auth.PasswordHasher.Hash(temporaryPassword);
        user.SecurityJson = UserSecurityProfile.SetStatus(
            user.SecurityJson,
            "password_reset_required",
            string.IsNullOrWhiteSpace(request.Reason) ? "admin_reset" : request.Reason.Trim());
        user.UpdatedAtUtc = DateTime.UtcNow;
        var activeSessions = await _db.UserSessions
            .Where(x => x.UserId == user.Id && x.RevokedAtUtc == null && x.ExpiresAtUtc > DateTime.UtcNow)
            .ToListAsync();
        foreach (var session in activeSessions)
        {
            session.RevokedAtUtc = DateTime.UtcNow;
            session.RevokedReason = "password_reset";
        }
        await _db.SaveChangesAsync();
        await _db.WriteAuditLogAsync(
            User,
            "users.password_reset",
            "user",
            user.Id,
            null,
            JsonSerializer.Serialize(new { user.Email }));

        return Ok(new { user.Id, temporaryPassword, reset = true });
    }

    [HttpGet("settings/{key}")]
    public async Task<IActionResult> GetSetting(string key)
    {
        var item = await _db.SystemSettings.FindAsync(key);
        if (item is null) return Ok(new { key, valueJson = "{}" });
        return Ok(new { key = item.Key, valueJson = item.ValueJson });
    }

    [HttpPut("settings/{key}")]
    public async Task<IActionResult> PutSetting(string key, [FromBody] AdminSettingRequest request)
    {
        var item = await _db.SystemSettings.FindAsync(key);
        if (item is null)
        {
            item = new SystemSetting { Key = key, ValueJson = request.ValueJson, UpdatedAtUtc = DateTime.UtcNow };
            _db.SystemSettings.Add(item);
        }
        else
        {
            item.ValueJson = request.ValueJson;
            item.UpdatedAtUtc = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        return Ok(new { key = item.Key, valueJson = item.ValueJson });
    }
}

