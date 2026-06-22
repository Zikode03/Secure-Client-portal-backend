using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application;
using SecureClientPortal.Backend.Application.Assignments;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Application.Contracts;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Models;
using System.Security.Claims;
using System.Text.Json;

namespace SecureClientPortal.Backend.Infrastructure.FirmManagement.Application;

public sealed class AssignmentService : IAssignmentService
{
    private readonly PortalDbContext _db;

    public AssignmentService(PortalDbContext db)
    {
        _db = db;
    }

    public async Task<(bool forbidden, IReadOnlyList<object> results)> GetAllAsync(ClaimsPrincipal user, string? clientId, CancellationToken ct = default)
    {
        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        var query = _db.ClientAssignments.AsQueryable();

        if (user.IsAdmin())
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
                    return (true, []);
                }

                query = query.Where(x => x.ClientId == clientId);
            }
        }

        var clients = await _db.Clients.ToDictionaryAsync(x => x.Id, ct);
        var users = await _db.Users.ToDictionaryAsync(x => x.Id, ct);
        var assignments = await query
            .OrderBy(x => x.ClientId)
            .ThenBy(x => x.CreatedAtUtc)
            .ToListAsync(ct);

        return (false, assignments.Select(assignment => (object)new
        {
            id = assignment.Id,
            clientId = assignment.ClientId,
            clientName = clients.GetValueOrDefault(assignment.ClientId)?.Name,
            accountantUserId = assignment.AccountantUserId,
            accountantName = users.GetValueOrDefault(assignment.AccountantUserId)?.FullName,
            isPrimary = string.Equals(
                clients.GetValueOrDefault(assignment.ClientId)?.AssignedAccountantId,
                assignment.AccountantUserId,
                StringComparison.OrdinalIgnoreCase),
            assignment.CreatedAtUtc
        }).ToArray());
    }

    public async Task<object> CreateAsync(CreateAssignmentRequest request, CurrentUserContext actor, CancellationToken ct = default)
    {
        var accountantRoleNames = await _db.RoleDefinitions
            .Where(x => x.Scope == "accountant" && x.IsActive)
            .Select(x => x.Name)
            .ToListAsync(ct);
        if (accountantRoleNames.Count == 0)
        {
            accountantRoleNames = ["accountant"];
        }
        var accountant = await _db.Users.FirstOrDefaultAsync(x => x.Id == request.AccountantUserId && accountantRoleNames.Contains(x.Role), ct);
        if (accountant is null)
        {
            throw new ArgumentException("Accountant user does not exist or is not an accountant.");
        }

        var client = await _db.Clients.FirstOrDefaultAsync(x => x.Id == request.ClientId, ct);
        if (client is null)
        {
            throw new ArgumentException("Client does not exist.");
        }

        var assignment = await _db.ClientAssignments.FirstOrDefaultAsync(x =>
            x.AccountantUserId == request.AccountantUserId && x.ClientId == request.ClientId, ct);
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

        await _db.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(
            actor.UserId,
            actor.RoleScope,
            "assignments.assigned",
            "client_assignment",
            assignment.Id,
            client.Id,
            JsonSerializer.Serialize(new { assignment.ClientId, assignment.AccountantUserId, request.IsPrimary }),
            ct);

        return new
        {
            assignment.Id,
            assignment.ClientId,
            assignment.AccountantUserId,
            isPrimary = string.Equals(client.AssignedAccountantId, assignment.AccountantUserId, StringComparison.OrdinalIgnoreCase)
        };
    }

    public async Task<bool> DeleteAsync(string id, CurrentUserContext actor, CancellationToken ct = default)
    {
        var assignment = await _db.ClientAssignments.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (assignment is null)
        {
            return false;
        }

        var client = await _db.Clients.FirstOrDefaultAsync(x => x.Id == assignment.ClientId, ct);
        if (client is null)
        {
            _db.ClientAssignments.Remove(assignment);
            await _db.SaveChangesAsync(ct);
            return true;
        }

        var isPrimary = string.Equals(client.AssignedAccountantId, assignment.AccountantUserId, StringComparison.OrdinalIgnoreCase);
        if (isPrimary)
        {
            var replacement = await _db.ClientAssignments
                .Where(x => x.ClientId == assignment.ClientId && x.Id != assignment.Id)
                .OrderBy(x => x.CreatedAtUtc)
                .FirstOrDefaultAsync(ct);
            if (replacement is null)
            {
                throw new InvalidOperationException("Cannot remove the only primary accountant assignment for a client.");
            }

            client.AssignedAccountantId = replacement.AccountantUserId;
            client.UpdatedAtUtc = DateTime.UtcNow;
        }

        _db.ClientAssignments.Remove(assignment);
        await _db.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(
            actor.UserId,
            actor.RoleScope,
            "assignments.removed",
            "client_assignment",
            assignment.Id,
            assignment.ClientId,
            JsonSerializer.Serialize(new { assignment.ClientId, assignment.AccountantUserId, wasPrimary = isPrimary }),
            ct);

        return true;
    }

    public async Task<object> ReassignAsync(ReassignAccountantRequest request, CurrentUserContext actor, CancellationToken ct = default)
    {
        var client = await _db.Clients.FirstOrDefaultAsync(x => x.Id == request.ClientId, ct);
        if (client is null)
        {
            throw new ArgumentException("Client does not exist.");
        }

        var accountantRoleNames = await _db.RoleDefinitions
            .Where(x => x.Scope == "accountant" && x.IsActive)
            .Select(x => x.Name)
            .ToListAsync(ct);
        if (accountantRoleNames.Count == 0)
        {
            accountantRoleNames = ["accountant"];
        }
        var targetAccountant = await _db.Users.FirstOrDefaultAsync(x => x.Id == request.ToAccountantUserId && accountantRoleNames.Contains(x.Role), ct);
        if (targetAccountant is null)
        {
            throw new ArgumentException("Target accountant user does not exist or is not an accountant.");
        }

        var existingTarget = await _db.ClientAssignments.FirstOrDefaultAsync(x =>
            x.ClientId == request.ClientId && x.AccountantUserId == request.ToAccountantUserId, ct);
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
            x.ClientId == request.ClientId && x.AccountantUserId == request.FromAccountantUserId, ct);
        if (previous is not null && !string.Equals(previous.AccountantUserId, existingTarget.AccountantUserId, StringComparison.OrdinalIgnoreCase))
        {
            _db.ClientAssignments.Remove(previous);
        }

        if (request.MakePrimary || string.Equals(client.AssignedAccountantId, request.FromAccountantUserId, StringComparison.OrdinalIgnoreCase))
        {
            client.AssignedAccountantId = request.ToAccountantUserId;
            client.UpdatedAtUtc = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(
            actor.UserId,
            actor.RoleScope,
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
            }),
            ct);

        return new
        {
            client.Id,
            previousAccountantUserId = request.FromAccountantUserId,
            currentAccountantUserId = request.ToAccountantUserId,
            isPrimary = string.Equals(client.AssignedAccountantId, request.ToAccountantUserId, StringComparison.OrdinalIgnoreCase)
        };
    }

    public async Task<object?> MakePrimaryAsync(string id, CurrentUserContext actor, CancellationToken ct = default)
    {
        var assignment = await _db.ClientAssignments.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (assignment is null)
        {
            return null;
        }

        var client = await _db.Clients.FirstOrDefaultAsync(x => x.Id == assignment.ClientId, ct);
        if (client is null)
        {
            throw new ArgumentException("Client does not exist.");
        }

        client.AssignedAccountantId = assignment.AccountantUserId;
        client.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(
            actor.UserId,
            actor.RoleScope,
            "assignments.primary_updated",
            "client_assignment",
            assignment.Id,
            assignment.ClientId,
            JsonSerializer.Serialize(new { assignment.ClientId, assignment.AccountantUserId }),
            ct);

        return new
        {
            assignment.Id,
            assignment.ClientId,
            assignment.AccountantUserId,
            isPrimary = true
        };
    }
}


