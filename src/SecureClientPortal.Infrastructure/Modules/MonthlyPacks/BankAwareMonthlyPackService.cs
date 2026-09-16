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
    public async Task<(bool forbidden, IReadOnlyList<MonthlyPack> items)> GetAllAsync(
        ClaimsPrincipal user,
        string? clientId = null,
        CancellationToken ct = default)
    {
        var result = await inner.GetAllAsync(user, clientId, ct);
        if (!result.forbidden)
            foreach (var pack in result.items)
                await ReconcileOrThrowAsync(pack, user, ct);
        return result;
    }

    public async Task<(bool forbidden, MonthlyPack? pack)> GetByClientAndPeriodAsync(
        string clientId,
        int year,
        int month,
        ClaimsPrincipal user,
        CancellationToken ct = default)
    {
        var result = await inner.GetByClientAndPeriodAsync(clientId, year, month, user, ct);
        if (!result.forbidden && result.pack is not null)
            await ReconcileOrThrowAsync(result.pack, user, ct);
        return result;
    }

    public async Task<(bool forbidden, MonthlyPack created)> CreateAsync(
        CreateMonthlyPackRequest request,
        ClaimsPrincipal user,
        CancellationToken ct = default)
    {
        var result = await inner.CreateAsync(request, user, ct);
        if (!result.forbidden && result.created is not null)
        {
            await ReconcileOrThrowAsync(result.created, user, ct);
        }
        return result;
    }

    public async Task<(bool forbidden, bool invalid, string? error, MonthlyPack? pack)> SubmitAsync(
        string id,
        ClaimsPrincipal user,
        CancellationToken ct = default)
    {
        var reconciliation = await ReconcileByPackIdAsync(id, user, ct);
        if (reconciliation?.Forbidden == true) return (true, false, null, null);
        if (reconciliation?.Success == false) return (false, true, "Bank-statement readiness could not be verified. Please retry.", null);
        return await inner.SubmitAsync(id, user, ct);
    }

    public async Task<(bool forbidden, bool invalid, string? error, MonthlyPack? pack)> CloseAsync(
        string id,
        ClaimsPrincipal user,
        CancellationToken ct = default)
    {
        var reconciliation = await ReconcileByPackIdAsync(id, user, ct);
        if (reconciliation?.Forbidden == true) return (true, false, null, null);
        if (reconciliation?.Success == false) return (false, true, "Bank-statement readiness could not be verified. Please retry.", null);
        return await inner.CloseAsync(id, user, ct);
    }

    private async Task<BankingOperationResult<bool>?> ReconcileByPackIdAsync(string id, ClaimsPrincipal user, CancellationToken ct)
    {
        if (!Guid.TryParse(id, out var packId)) return null;
        var pack = await db.MonthlyPacks
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == packId, ct);
        if (pack is null) return null;

        return await banking.ReconcileMonthlyPackBankSlotAsync(pack.ClientId, pack.Id, user, ct);
    }

    private async Task ReconcileOrThrowAsync(MonthlyPack pack, ClaimsPrincipal user, CancellationToken ct)
    {
        if (pack.Status is "under_review" or "complete" or "closed") return;
        var result = await banking.ReconcileMonthlyPackBankSlotAsync(pack.ClientId, pack.Id, user, ct);
        if (result.Forbidden || !result.Success)
            throw new InvalidOperationException("Bank-statement readiness could not be verified.");
    }
}
