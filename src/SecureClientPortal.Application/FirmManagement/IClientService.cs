using SecureClientPortal.Backend.Application.Contracts;
using SecureClientPortal.Backend.Models;
using System.Security.Claims;

namespace SecureClientPortal.Backend.Application.FirmManagement;

public interface IClientService
{
    Task<IReadOnlyList<Client>> GetAllAsync(ClaimsPrincipal user, CancellationToken ct = default);
    Task<(bool forbidden, Client? client)> GetByIdAsync(string id, ClaimsPrincipal user, CancellationToken ct = default);
    Task<(bool forbidden, Client created)> CreateAsync(Client request, ClaimsPrincipal user, CancellationToken ct = default);
    Task<(bool forbidden, Client? updated)> UpdateAsync(string id, Client request, ClaimsPrincipal user, CancellationToken ct = default);
    Task<(bool forbidden, Client? updated)> UpdateStatusAsync(string id, UpdateClientStatusRequest request, ClaimsPrincipal user, CancellationToken ct = default);
    Task<bool> DeleteAsync(string id, CancellationToken ct = default);
}
