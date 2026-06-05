using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Models;
using System.Text.Json;

namespace SecureClientPortal.Backend.Controllers;

public record CreateAssignmentRequest(string AccountantUserId, string ClientId, bool IsPrimary);
public record ReassignAccountantRequest(string ClientId, string FromAccountantUserId, string ToAccountantUserId, bool MakePrimary);

[ApiController]
[Route("api/assignments")]
[Authorize(Policy = "ClientOrAccountant")]
public class AssignmentsController : ControllerBase
{
    private readonly PortalDbContext _db;

    public AssignmentsController(PortalDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<object>>> GetAll([FromQuery] string? clientId = null)
    {
        var allowedClientIds = await User.GetAccessibleClientIdsAsync(_db);
        var query = _db.ClientAssignments.AsQueryable();

        if (User.IsAdmin())
        {
            if (!string.IsNullOrWhiteSpace(clientId))
            {
                query = query.Where(x => x.ClientId == clientId);
            }
        }
        else
        {
            query = query.Where(x => allowedClientIds.Contains(x.ClientId));
            if (!string.IsNullOrWhiteSpace(clientId))
            {
                if (!allowedClientIds.Contains(clientId))
                {
                    return Forbid();
                }

                query = query.Where(x => x.ClientId == clientId);
            }
        }

        var clients = await _db.Clients.ToDictionaryAsync(x => x.Id);
        var users = await _db.Users.ToDictionaryAsync(x => x.Id);
        var assignments = await query
            .OrderBy(x => x.ClientId)
            .ThenBy(x => x.CreatedAtUtc)
            .ToListAsync();

        return Ok(assignments.Select(assignment => new
        {
            assignment.Id,
            assignment.ClientId,
            clientName = clients.GetValueOrDefault(assignment.ClientId)?.Name,
            assignment.AccountantUserId,
            accountantName = users.GetValueOrDefault(assignment.AccountantUserId)?.FullName,
            isPrimary = string.Equals(
                clients.GetValueOrDefault(assignment.ClientId)?.AssignedAccountantId,
                assignment.AccountantUserId,
                StringComparison.OrdinalIgnoreCase),
            assignment.CreatedAtUtc
        }));
    }

    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Create([FromBody] CreateAssignmentRequest request)
    {
        var accountantRoleNames = await _db.RoleDefinitions
            .Where(x => x.Scope == "accountant" && x.IsActive)
            .Select(x => x.Name)
            .ToListAsync();
        var accountant = await _db.Users.FirstOrDefaultAsync(x => x.Id == request.AccountantUserId && accountantRoleNames.Contains(x.Role));
        if (accountant is null)
        {
            return BadRequest(new { error = "Accountant user does not exist or is not an accountant." });
        }

        var client = await _db.Clients.FirstOrDefaultAsync(x => x.Id == request.ClientId);
        if (client is null)
        {
            return BadRequest(new { error = "Client does not exist." });
        }

        var assignment = await _db.ClientAssignments.FirstOrDefaultAsync(x =>
            x.AccountantUserId == request.AccountantUserId && x.ClientId == request.ClientId);
        if (assignment is null)
        {
            assignment = new ClientAssignment
            {
                Id = $"ca_{Guid.NewGuid():N}",
                AccountantUserId = request.AccountantUserId,
                ClientId = request.ClientId,
                CreatedAtUtc = DateTime.UtcNow
            };
            _db.ClientAssignments.Add(assignment);
        }

        if (request.IsPrimary || string.IsNullOrWhiteSpace(client.AssignedAccountantId))
        {
            client.AssignedAccountantId = request.AccountantUserId;
            client.UpdatedAtUtc = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        await _db.WriteAuditLogAsync(
            User,
            "assignments.assigned",
            "client_assignment",
            assignment.Id,
            client.Id,
            JsonSerializer.Serialize(new { assignment.ClientId, assignment.AccountantUserId, request.IsPrimary }));

        return Created($"/api/assignments/{assignment.Id}", new
        {
            assignment.Id,
            assignment.ClientId,
            assignment.AccountantUserId,
            isPrimary = string.Equals(client.AssignedAccountantId, assignment.AccountantUserId, StringComparison.OrdinalIgnoreCase)
        });
    }

    [HttpDelete("{id}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Delete(string id)
    {
        var assignment = await _db.ClientAssignments.FirstOrDefaultAsync(x => x.Id == id);
        if (assignment is null)
        {
            return NotFound();
        }

        var client = await _db.Clients.FirstOrDefaultAsync(x => x.Id == assignment.ClientId);
        if (client is null)
        {
            _db.ClientAssignments.Remove(assignment);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        var isPrimary = string.Equals(client.AssignedAccountantId, assignment.AccountantUserId, StringComparison.OrdinalIgnoreCase);
        if (isPrimary)
        {
            var replacement = await _db.ClientAssignments
                .Where(x => x.ClientId == assignment.ClientId && x.Id != assignment.Id)
                .OrderBy(x => x.CreatedAtUtc)
                .FirstOrDefaultAsync();
            if (replacement is null)
            {
                return BadRequest(new { error = "Cannot remove the only primary accountant assignment for a client." });
            }

            client.AssignedAccountantId = replacement.AccountantUserId;
            client.UpdatedAtUtc = DateTime.UtcNow;
        }

        _db.ClientAssignments.Remove(assignment);
        await _db.SaveChangesAsync();
        await _db.WriteAuditLogAsync(
            User,
            "assignments.removed",
            "client_assignment",
            assignment.Id,
            assignment.ClientId,
            JsonSerializer.Serialize(new { assignment.ClientId, assignment.AccountantUserId, wasPrimary = isPrimary }));

        return NoContent();
    }

    [HttpPost("reassign")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Reassign([FromBody] ReassignAccountantRequest request)
    {
        var client = await _db.Clients.FirstOrDefaultAsync(x => x.Id == request.ClientId);
        if (client is null)
        {
            return BadRequest(new { error = "Client does not exist." });
        }

        var accountantRoleNames = await _db.RoleDefinitions
            .Where(x => x.Scope == "accountant" && x.IsActive)
            .Select(x => x.Name)
            .ToListAsync();
        var targetAccountant = await _db.Users.FirstOrDefaultAsync(x => x.Id == request.ToAccountantUserId && accountantRoleNames.Contains(x.Role));
        if (targetAccountant is null)
        {
            return BadRequest(new { error = "Target accountant user does not exist or is not an accountant." });
        }

        var existingTarget = await _db.ClientAssignments.FirstOrDefaultAsync(x =>
            x.ClientId == request.ClientId && x.AccountantUserId == request.ToAccountantUserId);
        if (existingTarget is null)
        {
            existingTarget = new ClientAssignment
            {
                Id = $"ca_{Guid.NewGuid():N}",
                ClientId = request.ClientId,
                AccountantUserId = request.ToAccountantUserId,
                CreatedAtUtc = DateTime.UtcNow
            };
            _db.ClientAssignments.Add(existingTarget);
        }

        var previous = await _db.ClientAssignments.FirstOrDefaultAsync(x =>
            x.ClientId == request.ClientId && x.AccountantUserId == request.FromAccountantUserId);
        if (previous is not null && !string.Equals(previous.AccountantUserId, existingTarget.AccountantUserId, StringComparison.OrdinalIgnoreCase))
        {
            _db.ClientAssignments.Remove(previous);
        }

        if (request.MakePrimary || string.Equals(client.AssignedAccountantId, request.FromAccountantUserId, StringComparison.OrdinalIgnoreCase))
        {
            client.AssignedAccountantId = request.ToAccountantUserId;
            client.UpdatedAtUtc = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        await _db.WriteAuditLogAsync(
            User,
            "assignments.reassigned",
            "client_assignment",
            existingTarget.Id,
            client.Id,
            JsonSerializer.Serialize(new
            {
                clientId = client.Id,
                fromAccountantUserId = request.FromAccountantUserId,
                toAccountantUserId = request.ToAccountantUserId,
                request.MakePrimary
            }));

        return Ok(new
        {
            client.Id,
            previousAccountantUserId = request.FromAccountantUserId,
            currentAccountantUserId = request.ToAccountantUserId,
            isPrimary = string.Equals(client.AssignedAccountantId, request.ToAccountantUserId, StringComparison.OrdinalIgnoreCase)
        });
    }

    [HttpPost("{id}/make-primary")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> MakePrimary(string id)
    {
        var assignment = await _db.ClientAssignments.FirstOrDefaultAsync(x => x.Id == id);
        if (assignment is null)
        {
            return NotFound();
        }

        var client = await _db.Clients.FirstOrDefaultAsync(x => x.Id == assignment.ClientId);
        if (client is null)
        {
            return BadRequest(new { error = "Client does not exist." });
        }

        client.AssignedAccountantId = assignment.AccountantUserId;
        client.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _db.WriteAuditLogAsync(
            User,
            "assignments.primary_updated",
            "client_assignment",
            assignment.Id,
            assignment.ClientId,
            JsonSerializer.Serialize(new { assignment.ClientId, assignment.AccountantUserId }));

        return Ok(new
        {
            assignment.Id,
            assignment.ClientId,
            assignment.AccountantUserId,
            isPrimary = true
        });
    }
}
