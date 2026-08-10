using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts.Modules.Clients;
using SecureClientPortal.Backend.Models;
using System.Security.Claims;

namespace SecureClientPortal.Backend.Application.Modules.Clients;

public interface IClientService
{
    Task<IReadOnlyList<Client>> GetAllAsync(ClaimsPrincipal user, CancellationToken ct = default);
    Task<(bool forbidden, Client? client)> GetByIdAsync(string id, ClaimsPrincipal user, CancellationToken ct = default);
    Task<(bool forbidden, Client created)> CreateAsync(Client request, ClaimsPrincipal user, CancellationToken ct = default);
    Task<(bool forbidden, Client? updated)> UpdateAsync(string id, Client request, ClaimsPrincipal user, CancellationToken ct = default);
    Task<(bool forbidden, Client? updated)> UpdateStatusAsync(string id, UpdateClientStatusRequest request, ClaimsPrincipal user, CancellationToken ct = default);
    Task<ServiceResult<ClientBusinessProfileResponse>> GetBusinessProfileAsync(string id, ClaimsPrincipal user, CancellationToken ct = default);
    Task<ServiceResult<ClientBusinessProfileResponse>> UpdateBusinessProfileAsync(string id, UpdateClientBusinessProfileRequest request, ClaimsPrincipal user, CancellationToken ct = default);
    Task<bool> DeleteAsync(string id, CancellationToken ct = default);
}
