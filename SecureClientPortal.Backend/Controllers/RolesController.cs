using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Models;
using System.Text.Json;

namespace SecureClientPortal.Backend.Controllers;

public record CreateRoleRequest(string Name, string DisplayName, string Scope, string[]? Permissions);
public record UpdateRoleRequest(string? DisplayName, string Scope, string[]? Permissions);

[ApiController]
[Route("api/roles")]
[Authorize(Policy = "AdminOnly")]
public class RolesController : ControllerBase
{
    private readonly PortalDbContext _db;

    public RolesController(PortalDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<object>>> GetAll()
    {
        var roles = await _db.RoleDefinitions
            .OrderBy(x => x.Scope)
            .ThenBy(x => x.DisplayName)
            .ToListAsync();

        return Ok(roles.Select(role => new
        {
            role.Name,
            role.DisplayName,
            role.Scope,
            permissions = RolePermissions.ParsePermissions(role.PermissionsJson, role.Name),
            role.IsSystemRole,
            role.IsActive,
            role.CreatedAtUtc,
            role.UpdatedAtUtc
        }));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRoleRequest request)
    {
        var roleName = NormalizeRoleName(request.Name);
        if (string.IsNullOrWhiteSpace(roleName))
        {
            return BadRequest(new { error = "Role name is required." });
        }

        if (await _db.RoleDefinitions.AnyAsync(x => x.Name == roleName))
        {
            return Conflict(new { error = "A role with this name already exists." });
        }

        var normalizedScope = RolePermissions.NormalizeScope(request.Scope);
        if (normalizedScope is not ("admin" or "accountant" or "client"))
        {
            return BadRequest(new { error = "Scope must be admin, accountant, or client." });
        }

        var role = new RoleDefinition
        {
            Name = roleName,
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? roleName : request.DisplayName.Trim(),
            Scope = normalizedScope,
            PermissionsJson = RolePermissions.SerializePermissions(
                request.Permissions?.Length > 0 ? request.Permissions : RolePermissions.ForRole(normalizedScope)),
            IsSystemRole = false,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _db.RoleDefinitions.Add(role);
        await _db.SaveChangesAsync();
        await _db.WriteAuditLogAsync(
            User,
            "roles.created",
            "role",
            role.Name,
            null,
            JsonSerializer.Serialize(new { role.Name, role.Scope }));

        return Created($"/api/roles/{role.Name}", new
        {
            role.Name,
            role.DisplayName,
            role.Scope,
            permissions = RolePermissions.ParsePermissions(role.PermissionsJson, role.Name),
            role.IsActive
        });
    }

    [HttpPut("{name}")]
    public async Task<IActionResult> Update(string name, [FromBody] UpdateRoleRequest request)
    {
        var role = await _db.RoleDefinitions.FirstOrDefaultAsync(x => x.Name == name);
        if (role is null)
        {
            return NotFound();
        }

        var normalizedScope = RolePermissions.NormalizeScope(request.Scope);
        if (normalizedScope is not ("admin" or "accountant" or "client"))
        {
            return BadRequest(new { error = "Scope must be admin, accountant, or client." });
        }

        role.DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? role.DisplayName : request.DisplayName.Trim();
        role.Scope = normalizedScope;
        role.PermissionsJson = RolePermissions.SerializePermissions(
            request.Permissions?.Length > 0 ? request.Permissions : RolePermissions.ForRole(normalizedScope));
        role.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _db.WriteAuditLogAsync(
            User,
            "roles.updated",
            "role",
            role.Name,
            null,
            JsonSerializer.Serialize(new { role.Name, role.Scope }));

        return Ok(new
        {
            role.Name,
            role.DisplayName,
            role.Scope,
            permissions = RolePermissions.ParsePermissions(role.PermissionsJson, role.Name),
            role.IsActive
        });
    }

    [HttpPost("{name}/activate")]
    public async Task<IActionResult> Activate(string name)
    {
        return await UpdateActivationAsync(name, true);
    }

    [HttpPost("{name}/deactivate")]
    public async Task<IActionResult> Deactivate(string name)
    {
        return await UpdateActivationAsync(name, false);
    }

    private async Task<IActionResult> UpdateActivationAsync(string name, bool isActive)
    {
        var role = await _db.RoleDefinitions.FirstOrDefaultAsync(x => x.Name == name);
        if (role is null)
        {
            return NotFound();
        }

        role.IsActive = isActive;
        role.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _db.WriteAuditLogAsync(
            User,
            isActive ? "roles.activated" : "roles.deactivated",
            "role",
            role.Name,
            null,
            JsonSerializer.Serialize(new { role.Name, isActive }));

        return Ok(new { role.Name, role.IsActive });
    }

    private static string NormalizeRoleName(string value)
    {
        return value.Trim().ToLowerInvariant().Replace(" ", "_");
    }
}
