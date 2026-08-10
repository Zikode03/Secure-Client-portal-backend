using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts.Modules.Clients;
using SecureClientPortal.Backend.Application.Modules.Clients;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Models;
using System.Security.Claims;
using System.Text.Json;

namespace SecureClientPortal.Backend.Infrastructure.Modules.Clients;

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
        if (!Guid.TryParse(id, out var clientId))
        {
            return (false, null);
        }

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(clientId)) return (true, null);
        var client = await _db.Clients.FindAsync([clientId], ct);
        return (false, client);
    }

    public async Task<(bool forbidden, Client created)> CreateAsync(Client request, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var actorId = user.GetUserId();
        var normalizedAssignedAccountantId = request.AssignedAccountantId;

        if (user.IsAccountant() && !user.IsAdmin())
        {
            if (normalizedAssignedAccountantId == Guid.Empty)
            {
                normalizedAssignedAccountantId = actorId ?? Guid.Empty;
            }
            else if (actorId.HasValue && normalizedAssignedAccountantId != actorId.Value)
            {
                return (true, null!);
            }
        }

        if (normalizedAssignedAccountantId == Guid.Empty)
        {
            throw new ArgumentException("Assigned accountant is required.");
        }

        var assignedAccountantRoleNames = await _db.RoleDefinitions.Where(x => x.Scope == "accountant" && x.IsActive).Select(x => x.Name).ToListAsync(ct);
        var assignedAccountantExists = await _db.Users.AnyAsync(x => x.Id == normalizedAssignedAccountantId && assignedAccountantRoleNames.Contains(x.Role), ct);
        if (!assignedAccountantExists)
        {
            throw new ArgumentException("Assigned accountant user does not exist or is not an accountant.");
        }

        var created = Client.Create(
            request.Id == Guid.Empty ? Guid.NewGuid() : request.Id,
            request.Name,
            request.EntityType,
            request.PrimaryContact,
            request.Email,
            FirmManagementDomainValues.ToClientStatus(NormalizeStatus(request.Status)));
        created.AssignAccountant(normalizedAssignedAccountantId);
        created.UpdateComplianceHealth(request.ComplianceHealth);

        _db.Clients.Add(created);
        _db.ClientAssignments.Add(ClientAssignment.Create(
            Guid.NewGuid(),
            created.AssignedAccountantId,
            created.Id,
            created.CreatedAtUtc));
        await _db.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(user, "clients.created", "client", created.Id, created.Id, JsonSerializer.Serialize(new { created.Name, created.AssignedAccountantId }), ct);

        return (false, created);
    }

    public async Task<(bool forbidden, Client? updated)> UpdateAsync(string id, Client request, ClaimsPrincipal user, CancellationToken ct = default)
    {
        if (!Guid.TryParse(id, out var clientId))
        {
            return (false, null);
        }

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(clientId)) return (true, null);

        var existing = await _db.Clients.FindAsync([clientId], ct);
        if (existing is null) return (false, null);

        existing.UpdateDetails(request.Name, request.EntityType, request.PrimaryContact, request.Email);
        existing.ChangeStatus(FirmManagementDomainValues.ToClientStatus(NormalizeStatus(request.Status)));
        existing.UpdateComplianceHealth(request.ComplianceHealth);
        existing.AssignAccountant(request.AssignedAccountantId);

        await _db.SaveChangesAsync(ct);
        return (false, existing);
    }

    public async Task<(bool forbidden, Client? updated)> UpdateStatusAsync(string id, UpdateClientStatusRequest request, ClaimsPrincipal user, CancellationToken ct = default)
    {
        if (!Guid.TryParse(id, out var clientId))
        {
            return (false, null);
        }

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(clientId)) return (true, null);

        var existing = await _db.Clients.FindAsync([clientId], ct);
        if (existing is null) return (false, null);

        existing.ChangeStatus(FirmManagementDomainValues.ToClientStatus(NormalizeStatus(request.Status)));
        await _db.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(user, "clients.status_updated", "client", existing.Id, existing.Id, JsonSerializer.Serialize(new { existing.Status }), ct);

        return (false, existing);
    }

    public async Task<ServiceResult<ClientBusinessProfileResponse>> GetBusinessProfileAsync(string id, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var result = await ResolveAccessibleClientAsync(id, user, ct);
        if (result.forbidden) return ServiceResult<ClientBusinessProfileResponse>.ForbiddenResult();
        return result.client is null
            ? ServiceResult<ClientBusinessProfileResponse>.NotFoundResult()
            : ServiceResult<ClientBusinessProfileResponse>.Success(MapBusinessProfile(result.client));
    }

    public async Task<ServiceResult<ClientBusinessProfileResponse>> UpdateBusinessProfileAsync(
        string id,
        UpdateClientBusinessProfileRequest request,
        ClaimsPrincipal user,
        CancellationToken ct = default)
    {
        var result = await ResolveAccessibleClientAsync(id, user, ct);
        if (result.forbidden) return ServiceResult<ClientBusinessProfileResponse>.ForbiddenResult();
        if (result.client is null) return ServiceResult<ClientBusinessProfileResponse>.NotFoundResult();

        try
        {
            result.client.UpdateBusinessProfile(
                request.LegalName,
                request.TradingName,
                request.RegistrationNumber,
                request.TaxNumber,
                request.VatNumber,
                request.PrimaryContact,
                request.FinanceEmail,
                request.Phone,
                request.AddressLine,
                request.City,
                request.Country,
                request.Industry,
                request.PrimaryContactJobTitle);
        }
        catch (DomainRuleException ex)
        {
            return ServiceResult<ClientBusinessProfileResponse>.ErrorResult(ex.Message);
        }

        await _db.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(
            user,
            "client.business_profile_updated",
            "client",
            result.client.Id,
            result.client.Id,
            JsonSerializer.Serialize(new
            {
                result.client.Name,
                result.client.TradingName,
                result.client.PrimaryContact,
                result.client.Email,
                result.client.Phone,
                result.client.City,
                result.client.Country
            }),
            ct);

        return ServiceResult<ClientBusinessProfileResponse>.Success(MapBusinessProfile(result.client));
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        if (!Guid.TryParse(id, out var clientId))
        {
            return false;
        }

        var existing = await _db.Clients.FindAsync([clientId], ct);
        if (existing is null) return false;

        var assignments = await _db.ClientAssignments.Where(x => x.ClientId == clientId).ToListAsync(ct);
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
            "archived" => "archived",
            "prospect" => "prospect",
            "at_risk" => "inactive",
            _ => "active"
        };
    }

    private async Task<(bool forbidden, Client? client)> ResolveAccessibleClientAsync(string id, ClaimsPrincipal user, CancellationToken ct)
    {
        if (!Guid.TryParse(id, out var clientId)) return (false, null);
        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(clientId)) return (true, null);
        return (false, await _db.Clients.FindAsync([clientId], ct));
    }

    private static ClientBusinessProfileResponse MapBusinessProfile(Client client) =>
        new(
            client.Id,
            client.Name,
            client.TradingName,
            client.RegistrationNumber,
            client.TaxNumber,
            client.VatNumber,
            client.PrimaryContact,
            client.Email,
            client.Phone,
            client.AddressLine,
            client.City,
            client.Country,
            client.Industry,
            client.PrimaryContactJobTitle,
            client.UpdatedAtUtc);
}
