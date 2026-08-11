using SecureClientPortal.Backend.Application.Contracts.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Domain.Modules.MonthlyPacks;
using System.Security.Claims;

namespace SecureClientPortal.Backend.Application.Modules.MonthlyPacks;

public interface IMonthlyPackService
{
    Task<(bool forbidden, IReadOnlyList<MonthlyPack> items)> GetAllAsync(ClaimsPrincipal user, string? clientId = null, CancellationToken ct = default);
    Task<(bool forbidden, MonthlyPack? pack)> GetByClientAndPeriodAsync(string clientId, int year, int month, ClaimsPrincipal user, CancellationToken ct = default);
    Task<(bool forbidden, MonthlyPack created)> CreateAsync(CreateMonthlyPackRequest request, ClaimsPrincipal user, CancellationToken ct = default);
    Task<(bool forbidden, bool invalid, string? error, MonthlyPack? pack)> SubmitAsync(string id, ClaimsPrincipal user, CancellationToken ct = default);
    Task<(bool forbidden, bool invalid, string? error, MonthlyPack? pack)> CloseAsync(string id, ClaimsPrincipal user, CancellationToken ct = default);
}
