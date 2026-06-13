using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;

namespace SecureClientPortal.Backend.Controllers;

public record UpdateClientStatusRequest(string Status);

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "ClientOrAccountant")]
public class ClientsController : ControllerBase
{
    private readonly PortalDbContext _db;

    public ClientsController(PortalDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Client>>> GetAll()
    {
        var allowedClientIds = await User.GetAccessibleClientIdsAsync(_db);
        return Ok(await _db.Clients
            .Where(x => allowedClientIds.Contains(x.Id))
            .OrderBy(x => x.Name)
            .ToListAsync());
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Client>> GetById(string id)
    {
        var allowedClientIds = await User.GetAccessibleClientIdsAsync(_db);
        if (!allowedClientIds.Contains(id))
        {
            return Forbid();
        }

        var client = await _db.Clients.FindAsync(id);
        if (client is null) return NotFound();
        return Ok(client);
    }

    [HttpPost]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<ActionResult<Client>> Create([FromBody] Client request)
    {
        var actorId = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? string.Empty;
        var normalizedAssignedAccountantId = request.AssignedAccountantId.Trim();
        if (User.IsAccountant() && !User.IsAdmin())
        {
            // Accountants can only create clients assigned to themselves.
            if (string.IsNullOrWhiteSpace(normalizedAssignedAccountantId))
            {
                normalizedAssignedAccountantId = actorId;
            }
            else if (!string.Equals(normalizedAssignedAccountantId, actorId, StringComparison.OrdinalIgnoreCase))
            {
                return Forbid();
            }
        }

        if (string.IsNullOrWhiteSpace(normalizedAssignedAccountantId))
        {
            return BadRequest(new { error = "Assigned accountant is required." });
        }

        var assignedAccountantRoleNames = await _db.RoleDefinitions
            .Where(x => x.Scope == "accountant" && x.IsActive)
            .Select(x => x.Name)
            .ToListAsync();
        var assignedAccountantExists = await _db.Users.AnyAsync(x =>
            x.Id == normalizedAssignedAccountantId && assignedAccountantRoleNames.Contains(x.Role));
        if (!assignedAccountantExists)
        {
            return BadRequest(new { error = "Assigned accountant user does not exist or is not an accountant." });
        }

        if (string.IsNullOrWhiteSpace(request.Id)) request.Id = $"c_{Guid.NewGuid():N}";
        request.AssignedAccountantId = normalizedAssignedAccountantId;
        request.Status = NormalizeStatus(request.Status);
        request.CreatedAtUtc = DateTime.UtcNow;
        request.UpdatedAtUtc = request.CreatedAtUtc;

        _db.Clients.Add(request);
        _db.ClientAssignments.Add(new ClientAssignment
        {
            Id = $"ca_{Guid.NewGuid():N}",
            AccountantUserId = request.AssignedAccountantId,
            ClientId = request.Id,
            CreatedAtUtc = request.CreatedAtUtc
        });
        await _db.SaveChangesAsync();
        await _db.WriteAuditLogAsync(
            User,
            "clients.created",
            "client",
            request.Id,
            request.Id,
            JsonSerializer.Serialize(new { request.Name, request.AssignedAccountantId }));

        return CreatedAtAction(nameof(GetById), new { id = request.Id }, request);
    }

    [HttpPut("{id}")]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<ActionResult<Client>> Update(string id, [FromBody] Client request)
    {
        var allowedClientIds = await User.GetAccessibleClientIdsAsync(_db);
        if (!allowedClientIds.Contains(id))
        {
            return Forbid();
        }

        var existing = await _db.Clients.FindAsync(id);
        if (existing is null) return NotFound();

        existing.Name = request.Name;
        existing.EntityType = request.EntityType;
        existing.Status = NormalizeStatus(request.Status);
        existing.ComplianceHealth = request.ComplianceHealth;
        existing.AssignedAccountantId = request.AssignedAccountantId;
        existing.PrimaryContact = request.PrimaryContact;
        existing.Email = request.Email;
        existing.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return Ok(existing);
    }

    [HttpPut("{id}/status")]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<ActionResult<Client>> UpdateStatus(string id, [FromBody] UpdateClientStatusRequest request)
    {
        var allowedClientIds = await User.GetAccessibleClientIdsAsync(_db);
        if (!allowedClientIds.Contains(id))
        {
            return Forbid();
        }

        var existing = await _db.Clients.FindAsync(id);
        if (existing is null)
        {
            return NotFound();
        }

        existing.Status = NormalizeStatus(request.Status);
        existing.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _db.WriteAuditLogAsync(
            User,
            "clients.status_updated",
            "client",
            existing.Id,
            existing.Id,
            JsonSerializer.Serialize(new { existing.Status }));

        return Ok(existing);
    }

    [HttpDelete("{id}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Delete(string id)
    {
        var existing = await _db.Clients.FindAsync(id);
        if (existing is null) return NotFound();

        var assignments = await _db.ClientAssignments.Where(x => x.ClientId == id).ToListAsync();
        if (assignments.Count > 0)
        {
            _db.ClientAssignments.RemoveRange(assignments);
        }
        _db.Clients.Remove(existing);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private static string NormalizeStatus(string? rawStatus)
    {
        var normalized = string.IsNullOrWhiteSpace(rawStatus) ? "active" : rawStatus.Trim().ToLowerInvariant();
        return normalized switch
        {
            "active" => "active",
            "inactive" => "inactive",
            "pending" => "inactive",
            "archived" => "inactive",
            "at_risk" => "inactive",
            _ => "active"
        };
    }
}
