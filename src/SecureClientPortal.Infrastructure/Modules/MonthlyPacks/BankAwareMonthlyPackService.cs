using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application.Contracts.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Application.Modules.Banking;
using SecureClientPortal.Backend.Application.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Domain.Modules.MonthlyPacks;
using System.Security.Claims;

namespace SecureClientPortal.Backend.Infrastructure.Modules.MonthlyPacks;

/// <summary>
/// Keeps the established monthly-pack workflow intact while applying Banking's
/// source-of-truth state whenever a pack is created, submitted, or closed.
/// </summary>
public sealed class BankAwareMonthlyPackService(
    MonthlyPackService inner,
    IBankingService banking,
    PortalDbContext db) : IMonthlyPackService
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

    public async Task<(bool forbidden, bool invalid, string? error, MonthlyPack? pack)> SubmitAsync(
        string id,
        ClaimsPrincipal user,
        CancellationToken ct = default)
    {
        await ReconcileByPackIdAsync(id, user, ct);
        return await inner.SubmitAsync(id, user, ct);
    }

    public async Task<(bool forbidden, bool invalid, string? error, MonthlyPack? pack)> CloseAsync(
        string id,
        ClaimsPrincipal user,
        CancellationToken ct = default)
    {
        await ReconcileByPackIdAsync(id, user, ct);
        return await inner.CloseAsync(id, user, ct);
    }

    private async Task ReconcileByPackIdAsync(string id, ClaimsPrincipal user, CancellationToken ct)
    {
        if (!Guid.TryParse(id, out var packId)) return;
        var pack = await db.MonthlyPacks
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == packId, ct);
        if (pack is null) return;

        await banking.ReconcileMonthlyPackBankSlotAsync(pack.ClientId, pack.Id, user, ct);
    }
}
