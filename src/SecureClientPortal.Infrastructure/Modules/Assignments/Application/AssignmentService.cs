using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application;
using SecureClientPortal.Backend.Application.Contracts.Modules.Assignments;
using SecureClientPortal.Backend.Application.Modules.Assignments;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Domain.Modules.Assignments;
using SecureClientPortal.Backend.Models;
using System.Security.Claims;
using System.Text.Json;

namespace SecureClientPortal.Backend.Infrastructure.Modules.Assignments.Application;

public sealed class AssignmentService : IAssignmentService
{
    private readonly PortalDbContext _db;
    private readonly AssignmentDomainService _assignmentDomainService;

    public AssignmentService(PortalDbContext db)
    {
        _db = db;
        _assignmentDomainService = new AssignmentDomainService();
    }

    public async Task<(bool forbidden, IReadOnlyList<AssignmentResponse> results)> GetAllAsync(ClaimsPrincipal user, string? clientId, CancellationToken ct = default)
    {
        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        var query = _db.ClientAssignments.AsQueryable();
        var hasClientFilter = Guid.TryParse(clientId, out var parsedClientId);

        if (user.IsAdmin())
        {
            if (hasClientFilter)
            {
                query = query.Where(x => x.ClientId == parsedClientId);
            }
        }
        else
        {
            query = query.Where(x => allowedClientIds.Contains(x.ClientId));
            if (!string.IsNullOrWhiteSpace(clientId))
            {
                if (!hasClientFilter || !allowedClientIds.Contains(parsedClientId))
                {
                    return (true, []);
                }

                query = query.Where(x => x.ClientId == parsedClientId);
            }
        }

        var clients = await _db.Clients.ToDictionaryAsync(x => x.Id, ct);
        var users = await _db.Users.ToDictionaryAsync(x => x.Id, ct);
        var assignments = await query
            .OrderBy(x => x.ClientId)
            .ThenBy(x => x.CreatedAtUtc)
            .ToListAsync(ct);

        return (false, assignments.Select(assignment => new AssignmentResponse(
            assignment.Id, assignment.ClientId, clients.GetValueOrDefault(assignment.ClientId)?.Name,
            assignment.AccountantUserId, users.GetValueOrDefault(assignment.AccountantUserId)?.FullName,
            clients.GetValueOrDefault(assignment.ClientId)?.AssignedAccountantId == assignment.AccountantUserId,
            assignment.CreatedAtUtc)).ToArray());
    }

    public async Task<object> CreateAsync(CreateAssignmentRequest request, CurrentUserContext actor, CancellationToken ct = default)
    {

        await using var gate = await AccountAttemptLock.AcquireAsync(_db, "assignments:" + request.ClientId.ToString(), ct);
        ValidateHandover(request.Handover);
        var accountantRoleNames = await _db.RoleDefinitions
            .Where(x => x.Scope == "accountant" && x.IsActive)
            .Select(x => x.Name)
            .ToListAsync(ct);
        if (accountantRoleNames.Count == 0 && !await _db.RoleDefinitions.AnyAsync(x => x.Name == "accountant", ct))
        {
            accountantRoleNames = ["accountant"];
        }

        var accountant = await _db.Users.FirstOrDefaultAsync(x => x.Id == request.AccountantUserId && accountantRoleNames.Contains(x.Role), ct);
        if (accountant is null || UserSecurityProfile.GetStatus(accountant.SecurityJson) != "active")
        {
            throw new ArgumentException("Choose an active accountant. Invited, disabled and locked accounts cannot receive assignments.");
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
            assignment = ClientAssignment.Create(
                Guid.NewGuid(),
                request.AccountantUserId,
                request.ClientId);
            _db.ClientAssignments.Add(assignment);
        }

        if (request.IsPrimary || client.AssignedAccountantId == Guid.Empty)
        {
            client.AssignAccountant(request.AccountantUserId);
        }

        var effectiveAtUtc = DateTime.UtcNow;
        StageAssignmentAudit(actor, "assignments.assigned", assignment.Id, client.Id,
            new { assignment.ClientId, assignment.AccountantUserId, request.IsPrimary, effectiveAtUtc,
                reason = request.Handover?.Reason.Trim(), message = request.Handover?.Message.Trim() });
        StageHandover(request.Handover, client, accountant.Id, effectiveAtUtc);
        await _db.SaveChangesAsync(ct);

        return new
        {
            assignment.Id,
            assignment.ClientId,
            assignment.AccountantUserId,
            isPrimary = client.AssignedAccountantId == assignment.AccountantUserId
        };
    }

    public async Task<bool> DeleteAsync(string id, CurrentUserContext actor, CancellationToken ct = default)
    {
        if (!Guid.TryParse(id, out var assignmentId))
        {
            return false;
        }

        var assignment = await _db.ClientAssignments.FirstOrDefaultAsync(x => x.Id == assignmentId, ct);
        if (assignment is null)
        {
            return false;
        }

        await using var gate = await AccountAttemptLock.AcquireAsync(_db, "assignments:" + assignment.ClientId.ToString(), ct);
        await _db.Entry(assignment).ReloadAsync(ct);
        if (_db.Entry(assignment).State == EntityState.Detached) return false;
        var client = await _db.Clients.FirstOrDefaultAsync(x => x.Id == assignment.ClientId, ct);
        if (client is null)
        {
            _db.ClientAssignments.Remove(assignment);
            await _db.SaveChangesAsync(ct);
            return true;
        }

        var candidates = await _db.ClientAssignments.Where(x => x.ClientId == assignment.ClientId).ToListAsync(ct);
        if (client.AssignedAccountantId == assignment.AccountantUserId)
        {
            var ids = candidates.Select(x => x.AccountantUserId).ToArray();
            var people = await _db.Users.Where(x => ids.Contains(x.Id)).ToListAsync(ct);
            var roles = await _db.RoleDefinitions.Where(x => x.IsActive && x.Scope == "accountant").Select(x => x.Name).ToListAsync(ct);
            if (roles.Count == 0 && !await _db.RoleDefinitions.AnyAsync(x => x.Name == "accountant", ct)) roles = ["accountant"];
            var activeIds = people.Where(x => roles.Contains(x.Role) && UserSecurityProfile.GetStatus(x.SecurityJson) == "active").Select(x => x.Id).ToHashSet();
            candidates = candidates.Where(x => x.Id == assignment.Id || activeIds.Contains(x.AccountantUserId)).ToList();
        }
        var replacement = _assignmentDomainService.SelectReplacementPrimary(client, assignment, candidates);
        var isPrimary = replacement is not null;
        if (replacement is not null)
        {
            client.AssignAccountant(replacement.AccountantUserId);
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

        await using var gate = await AccountAttemptLock.AcquireAsync(_db, "assignments:" + request.ClientId.ToString(), ct);
        ValidateHandover(request.Handover);
        var client = await _db.Clients.FirstOrDefaultAsync(x => x.Id == request.ClientId, ct);
        if (client is null)
        {
            throw new ArgumentException("Client does not exist.");
        }

        var accountantRoleNames = await _db.RoleDefinitions
            .Where(x => x.Scope == "accountant" && x.IsActive)
            .Select(x => x.Name)
            .ToListAsync(ct);
        if (accountantRoleNames.Count == 0 && !await _db.RoleDefinitions.AnyAsync(x => x.Name == "accountant", ct))
        {
            accountantRoleNames = ["accountant"];
        }

        var targetAccountant = await _db.Users.FirstOrDefaultAsync(x => x.Id == request.ToAccountantUserId && accountantRoleNames.Contains(x.Role), ct);
        if (targetAccountant is null || UserSecurityProfile.GetStatus(targetAccountant.SecurityJson) != "active")
        {
            throw new ArgumentException("Choose an active accountant. Invited, disabled and locked accounts cannot receive assignments.");
        }

        var previous = await _db.ClientAssignments.FirstOrDefaultAsync(x =>
            x.ClientId == request.ClientId && x.AccountantUserId == request.FromAccountantUserId, ct);
        if (previous is null || request.FromAccountantUserId == request.ToAccountantUserId)
            throw new ArgumentException("The assignment changed. Refresh the client coverage before trying again.");
        if (request.MakePrimary && client.AssignedAccountantId != request.FromAccountantUserId)
            throw new ArgumentException("The primary accountant changed. Refresh client coverage before trying again.");
        if (!request.MakePrimary && (client.AssignedAccountantId == request.FromAccountantUserId || client.AssignedAccountantId == request.ToAccountantUserId))
            throw new ArgumentException("Backup replacement cannot change or duplicate the primary accountant.");

        var existingTarget = await _db.ClientAssignments.FirstOrDefaultAsync(x =>
            x.ClientId == request.ClientId && x.AccountantUserId == request.ToAccountantUserId, ct);
        if (existingTarget is null)
        {
            existingTarget = ClientAssignment.Create(
                Guid.NewGuid(),
                request.ToAccountantUserId,
                request.ClientId);
            _db.ClientAssignments.Add(existingTarget);
        }

        _db.ClientAssignments.Remove(previous);

        if (request.MakePrimary || client.AssignedAccountantId == request.FromAccountantUserId)
        {
            client.AssignAccountant(request.ToAccountantUserId);
        }

        var effectiveAtUtc = DateTime.UtcNow;
        StageAssignmentAudit(actor, "assignments.reassigned", existingTarget.Id, client.Id,
            new { clientId = client.Id, fromAccountantUserId = request.FromAccountantUserId,
                toAccountantUserId = request.ToAccountantUserId, request.MakePrimary, effectiveAtUtc,
                reason = request.Handover?.Reason.Trim(), message = request.Handover?.Message.Trim() });
        StageHandover(request.Handover, client, targetAccountant.Id, effectiveAtUtc);
        await _db.SaveChangesAsync(ct);

        return new
        {
            client.Id,
            previousAccountantUserId = request.FromAccountantUserId,
            currentAccountantUserId = request.ToAccountantUserId,
            isPrimary = client.AssignedAccountantId == request.ToAccountantUserId
        };
    }

    public async Task<object?> MakePrimaryAsync(string id, CurrentUserContext actor, CancellationToken ct = default)
    {
        if (!Guid.TryParse(id, out var assignmentId))
        {
            return null;
        }

        var assignment = await _db.ClientAssignments.FirstOrDefaultAsync(x => x.Id == assignmentId, ct);
        if (assignment is null)
        {
            return null;
        }

        await using var gate = await AccountAttemptLock.AcquireAsync(_db, "assignments:" + assignment.ClientId.ToString(), ct);
        await _db.Entry(assignment).ReloadAsync(ct);
        if (_db.Entry(assignment).State == EntityState.Detached) return null;
        var client = await _db.Clients.FirstOrDefaultAsync(x => x.Id == assignment.ClientId, ct);
        if (client is null)
        {
            throw new ArgumentException("Client does not exist.");
        }

        var target = await _db.Users.SingleOrDefaultAsync(x => x.Id == assignment.AccountantUserId, ct);
        if (target is null || UserSecurityProfile.GetStatus(target.SecurityJson) != "active")
            throw new ArgumentException("Choose an active accountant.");
        var targetRole = await _db.RoleDefinitions.SingleOrDefaultAsync(x => x.Name == target.Role, ct);
        if (targetRole is not null ? !targetRole.IsActive || targetRole.Scope != "accountant" : target.Role != "accountant")
            throw new ArgumentException("Choose an active accountant role.");
        client.AssignAccountant(assignment.AccountantUserId);
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
    private static void ValidateHandover(AssignmentHandoverRequest? handover)
    {
        if (handover is null) return;
        if (string.IsNullOrWhiteSpace(handover.Reason) || string.IsNullOrWhiteSpace(handover.Message))
            throw new ArgumentException("Provide an assignment reason and handover message.");
        if (handover.Reason.Length > 1000 || handover.Message.Length > 1000)
            throw new ArgumentException("Keep the reason under 1,000 characters and message under 1,000 characters.");
    }
    private void StageAssignmentAudit(CurrentUserContext actor, string action, Guid assignmentId, Guid clientId, object metadata) =>
        _db.AuditLogs.Add(AuditLog.Create(Guid.NewGuid(), actor.UserId, actor.RoleScope, action,
            "client_assignment", assignmentId, clientId, JsonSerializer.Serialize(metadata)));
    private void StageHandover(AssignmentHandoverRequest? handover, Client client, Guid accountantId, DateTime effectiveAtUtc)
    {
        if (handover is null) return;
        _db.Notifications.Add(Notification.Create(Guid.NewGuid(), accountantId, client.Id, "assignment_handover",
            "Client handover: " + client.Name, handover.Message.Trim(),
            "/firm/clients/" + client.Id, effectiveAtUtc));
    }

}
