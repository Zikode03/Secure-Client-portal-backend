using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application.Contracts;
using SecureClientPortal.Backend.Application.FirmManagement;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Models;
using System.Security.Claims;
using System.Text.Json;

namespace SecureClientPortal.Backend.Infrastructure.FirmManagement;

public sealed class ClientService : IClientService
{
    private readonly PortalDbContext _db;

    public ClientService(PortalDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<Client>> GetAllAsync(ClaimsPrincipal user, CancellationToken ct = default)
    {
        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        return await _db.Clients.Where(x => allowedClientIds.Contains(x.Id)).OrderBy(x => x.Name).ToListAsync(ct);
    }

    public async Task<(bool forbidden, Client? client)> GetByIdAsync(string id, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(id)) return (true, null);
        var client = await _db.Clients.FindAsync([id], ct);
        return (false, client);
    }

    public async Task<(bool forbidden, Client created)> CreateAsync(Client request, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var actorId = user.FindFirst("sub")?.Value ?? string.Empty;
        var normalizedAssignedAccountantId = request.AssignedAccountantId.Trim();
        if (user.IsAccountant() && !user.IsAdmin())
        {
            if (string.IsNullOrWhiteSpace(normalizedAssignedAccountantId))
            {
                normalizedAssignedAccountantId = actorId;
            }
            else if (!string.Equals(normalizedAssignedAccountantId, actorId, StringComparison.OrdinalIgnoreCase))
            {
                return (true, null!);
            }
        }

        if (string.IsNullOrWhiteSpace(normalizedAssignedAccountantId))
        {
            throw new ArgumentException("Assigned accountant is required.");
        }

        var assignedAccountantRoleNames = await _db.RoleDefinitions.Where(x => x.Scope == "accountant" && x.IsActive).Select(x => x.Name).ToListAsync(ct);
        var assignedAccountantExists = await _db.Users.AnyAsync(x => x.Id == normalizedAssignedAccountantId && assignedAccountantRoleNames.Contains(x.Role), ct);
        if (!assignedAccountantExists)
        {
            throw new ArgumentException("Assigned accountant user does not exist or is not an accountant.");
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
        await _db.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(user, "clients.created", "client", request.Id, request.Id, JsonSerializer.Serialize(new { request.Name, request.AssignedAccountantId }), ct);

        return (false, request);
    }

    public async Task<(bool forbidden, Client? updated)> UpdateAsync(string id, Client request, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(id)) return (true, null);

        var existing = await _db.Clients.FindAsync([id], ct);
        if (existing is null) return (false, null);

        existing.Name = request.Name;
        existing.EntityType = request.EntityType;
        existing.Status = NormalizeStatus(request.Status);
        existing.ComplianceHealth = request.ComplianceHealth;
        existing.AssignedAccountantId = request.AssignedAccountantId;
        existing.PrimaryContact = request.PrimaryContact;
        existing.Email = request.Email;
        existing.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return (false, existing);
    }

    public async Task<(bool forbidden, Client? updated)> UpdateStatusAsync(string id, UpdateClientStatusRequest request, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(id)) return (true, null);

        var existing = await _db.Clients.FindAsync([id], ct);
        if (existing is null) return (false, null);

        existing.Status = NormalizeStatus(request.Status);
        existing.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(user, "clients.status_updated", "client", existing.Id, existing.Id, JsonSerializer.Serialize(new { existing.Status }), ct);

        return (false, existing);
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        var existing = await _db.Clients.FindAsync([id], ct);
        if (existing is null) return false;

        var assignments = await _db.ClientAssignments.Where(x => x.ClientId == id).ToListAsync(ct);
        if (assignments.Count > 0)
        {
            _db.ClientAssignments.RemoveRange(assignments);
        }
        _db.Clients.Remove(existing);
        await _db.SaveChangesAsync(ct);
        return true;
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
