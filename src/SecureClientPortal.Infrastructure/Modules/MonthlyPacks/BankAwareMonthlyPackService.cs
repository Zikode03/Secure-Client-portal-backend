using SecureClientPortal.Backend.Application.Contracts.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Application.Modules.Banking;
using SecureClientPortal.Backend.Application.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Domain.Modules.MonthlyPacks;
using System.Security.Claims;

namespace SecureClientPortal.Backend.Infrastructure.Modules.MonthlyPacks;

/// <summary>
/// Keeps the established monthly-pack workflow intact while applying Banking's
/// source-of-truth state whenever a new pack is created.
/// </summary>
public sealed class BankAwareMonthlyPackService(
    MonthlyPackService inner,
    IBankingService banking) : IMonthlyPackService
{
    public Task<(bool forbidden, IReadOnlyList<MonthlyPack> items)> GetAllAsync(
        ClaimsPrincipal user,
        string? clientId = null,
        CancellationToken ct = default) =>
        inner.GetAllAsync(user, clientId, ct);

    public Task<(bool forbidden, MonthlyPack? pack)> GetByClientAndPeriodAsync(
        string clientId,
        int year,
        int month,
        ClaimsPrincipal user,
        CancellationToken ct = default) =>
        inner.GetByClientAndPeriodAsync(clientId, year, month, user, ct);

    public async Task<(bool forbidden, MonthlyPack created)> CreateAsync(
        CreateMonthlyPackRequest request,
        ClaimsPrincipal user,
        CancellationToken ct = default)
    {
        var result = await inner.CreateAsync(request, user, ct);
        if (!result.forbidden && result.created is not null)
        {
            await banking.ReconcileMonthlyPackBankSlotAsync(
                result.created.ClientId,
                result.created.Id,
                user,
                ct);
        }
        return result;
    }

    public Task<(bool forbidden, bool invalid, string? error, MonthlyPack? pack)> SubmitAsync(
        string id,
        ClaimsPrincipal user,
        CancellationToken ct = default) =>
        inner.SubmitAsync(id, user, ct);

    public Task<(bool forbidden, bool invalid, string? error, MonthlyPack? pack)> CloseAsync(
        string id,
        ClaimsPrincipal user,
        CancellationToken ct = default) =>
        inner.CloseAsync(id, user, ct);
}
