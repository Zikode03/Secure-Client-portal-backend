using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application.Contracts.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Application.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Domain.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Domain.Shared.Modules.MonthlyPacks;
using System.Security.Claims;
using System.Text.Json;

namespace SecureClientPortal.Backend.Infrastructure.Modules.MonthlyPacks;

public sealed class MonthlyPackService : IMonthlyPackService
{
    private readonly PortalDbContext _db;

    public MonthlyPackService(PortalDbContext db)
    {
        _db = db;
    }

    public async Task<(bool forbidden, IReadOnlyList<MonthlyPack> items)> GetAllAsync(ClaimsPrincipal user, string? clientId = null, CancellationToken ct = default)
    {
        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        var query = _db.MonthlyPacks.Where(x => allowedClientIds.Contains(x.ClientId));
        if (Guid.TryParse(clientId, out var parsedClientId))
        {
            if (!allowedClientIds.Contains(parsedClientId))
            {
                return (true, []);
            }

            query = query.Where(x => x.ClientId == parsedClientId);
        }

        var items = await query.OrderByDescending(x => x.Year).ThenByDescending(x => x.Month).ToListAsync(ct);
        return (false, items);
    }

    public async Task<(bool forbidden, MonthlyPack? pack)> GetByClientAndPeriodAsync(string clientId, int year, int month, ClaimsPrincipal user, CancellationToken ct = default)
    {
        if (!Guid.TryParse(clientId, out var parsedClientId))
        {
            return (false, null);
        }

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(parsedClientId))
        {
            return (true, null);
        }

        var pack = await _db.MonthlyPacks.FirstOrDefaultAsync(x => x.ClientId == parsedClientId && x.Year == year && x.Month == month, ct);
        return (false, pack);
    }

    public async Task<(bool forbidden, MonthlyPack created)> CreateAsync(CreateMonthlyPackRequest request, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(request.ClientId) && !user.IsAdmin())
        {
            return (true, null!);
        }

        var existing = await _db.MonthlyPacks.FirstOrDefaultAsync(x => x.ClientId == request.ClientId && x.Year == request.Year && x.Month == request.Month, ct);
        if (existing is not null)
        {
            return (false, existing);
        }

        var pack = MonthlyPack.Create(
            Guid.NewGuid(),
            request.ClientId,
            request.Year,
            request.Month,
            DateTime.UtcNow);
        ApplyStatus(pack, NormalizeStatus(request.Status));

        _db.MonthlyPacks.Add(pack);
        await _db.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(
            user,
            "monthly_packs.created",
            "monthly_pack",
            pack.Id,
            pack.ClientId,
            JsonSerializer.Serialize(new { pack.ClientId, pack.Year, pack.Month, pack.Status }),
            ct);
        return (false, pack);
    }

    public async Task<(bool forbidden, bool invalid, string? error, MonthlyPack? pack)> CloseAsync(string id, ClaimsPrincipal user, CancellationToken ct = default)
    {
        if (!Guid.TryParse(id, out var monthlyPackId))
        {
            return (false, false, null, null);
        }

        var pack = await _db.MonthlyPacks.FirstOrDefaultAsync(x => x.Id == monthlyPackId, ct);
        if (pack is null)
        {
            return (false, false, null, null);
        }

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(pack.ClientId))
        {
            return (true, false, null, null);
        }

        if (!user.IsAdmin() && !user.IsAccountant())
        {
            return (true, false, null, null);
        }

        var slots = await _db.DocumentSlots.Where(x => x.MonthlyPackId == pack.Id).ToListAsync(ct);
        var requiredApplicableComplete = slots
            .Where(x => x.IsRequired && x.Status != "not_applicable")
            .All(x => x.Status == "accepted");

        if (!requiredApplicableComplete)
        {
            return (false, true, "All applicable required slots must be accepted before the monthly pack can be closed.", null);
        }

        pack.Close();
        await _db.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(
            user,
            "monthly_packs.closed",
            "monthly_pack",
            pack.Id,
            pack.ClientId,
            JsonSerializer.Serialize(new { pack.Id, pack.ClientId, pack.Year, pack.Month, pack.Status }),
            ct);

        return (false, false, null, pack);
    }

    private static void ApplyStatus(MonthlyPack pack, string status)
    {
        switch (status)
        {
            case "not_started":
                pack.MarkNotStarted();
                break;
            case "in_progress":
                pack.MarkInProgress();
                break;
            case "partially_submitted":
                pack.MarkPartiallySubmitted();
                break;
            case "under_review":
                pack.MarkUnderReview();
                break;
            case "complete":
                pack.Complete();
                break;
            case "closed":
                pack.Close();
                break;
            default:
                pack.MarkNotStarted();
                break;
        }
    }

    private static string NormalizeStatus(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "not_started" : value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "not_started" => "not_started",
            "draft" => "not_started",
            "in_progress" => "in_progress",
            "partially_submitted" => "partially_submitted",
            "submitted" => "partially_submitted",
            "under_review" => "under_review",
            "complete" => "complete",
            "completed" => "complete",
            "closed" => "closed",
            _ => "not_started"
        };
    }
}

