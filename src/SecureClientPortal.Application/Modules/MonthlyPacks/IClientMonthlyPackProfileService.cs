using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts.Modules.MonthlyPacks;
using System.Security.Claims;

namespace SecureClientPortal.Backend.Application.Modules.MonthlyPacks;

public interface IClientMonthlyPackProfileService
{
    Task<ServiceResult<ClientMonthlyPackProfileDto>> GetAsync(Guid clientId, ClaimsPrincipal user, CancellationToken ct = default);
    Task<ServiceResult<ClientMonthlyPackProfileDto>> UpdateAsync(Guid clientId, UpdateClientMonthlyPackProfileRequest request, ClaimsPrincipal user, CancellationToken ct = default);
    Task<ServiceResult<AddClientMonthlyPackItemResponse>> AddItemAsync(Guid clientId, AddClientMonthlyPackItemRequest request, ClaimsPrincipal user, CancellationToken ct = default);
    Task<ServiceResult<ClientMonthlyPackProfileDto>> ApproveRecurringAsync(Guid clientId, Guid requestId, ClaimsPrincipal user, CancellationToken ct = default);
    Task<ServiceResult<ClientMonthlyPackProfileDto>> DeclineRecurringAsync(Guid clientId, Guid requestId, ClaimsPrincipal user, CancellationToken ct = default);

    // Applies the effective recurring profile (firm template + client-specific items) to a pack.
    // MonthlyPackService calls this immediately after creating a new monthly pack.
    Task ApplyProfileToPackAsync(Guid clientId, Guid monthlyPackId, CancellationToken ct = default);
}
