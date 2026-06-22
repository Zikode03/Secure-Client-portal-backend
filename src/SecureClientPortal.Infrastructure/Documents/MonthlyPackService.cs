using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application.Contracts;
using SecureClientPortal.Backend.Application.Documents;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Models;
using System.Security.Claims;
using System.Text.Json;

namespace SecureClientPortal.Backend.Infrastructure.Documents;

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
        if (!string.IsNullOrWhiteSpace(clientId))
        {
            if (!allowedClientIds.Contains(clientId))
            {
                return (true, []);
            }

            query = query.Where(x => x.ClientId == clientId);
        }

        var items = await query.OrderByDescending(x => x.Year).ThenByDescending(x => x.Month).ToListAsync(ct);
        return (false, items);
    }

    public async Task<(bool forbidden, MonthlyPack? pack)> GetByClientAndPeriodAsync(string clientId, int year, int month, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(clientId))
        {
            return (true, null);
        }

        var pack = await _db.MonthlyPacks.FirstOrDefaultAsync(x => x.ClientId == clientId && x.Year == year && x.Month == month, ct);
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

        var pack = new MonthlyPack
        {
            Id = $"mp_{Guid.NewGuid():N}",
            ClientId = request.ClientId,
            Year = request.Year,
            Month = request.Month,
            Status = NormalizeStatus(request.Status),
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

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

    public async Task<(bool forbidden, bool invalid, string? error, MonthlyPack? pack)> SubmitAsync(string id, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var pack = await _db.MonthlyPacks.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (pack is null)
        {
            return (false, false, null, null);
        }

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(pack.ClientId))
        {
            return (true, false, null, null);
        }

        var requiredSlots = await _db.DocumentSlots.Where(x => x.MonthlyPackId == pack.Id && x.IsRequired).ToListAsync(ct);
        if (requiredSlots.Any(x => x.Status == "missing"))
        {
            return (false, true, "All required document slots must be uploaded before submission.", null);
        }

        pack.Status = "submitted";
        pack.SubmittedAtUtc = DateTime.UtcNow;
        pack.UpdatedAtUtc = pack.SubmittedAtUtc.Value;
        await _db.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(
            user,
            "monthly_packs.submitted",
            "monthly_pack",
            pack.Id,
            pack.ClientId,
            JsonSerializer.Serialize(new { pack.ClientId, pack.Year, pack.Month, pack.Status }),
            ct);

        var recipients = await _db.ResolveNotificationRecipientsAsync(pack.ClientId, "accountant", ct);
        await _db.AddNotificationsAsync(
            user,
            recipients,
            pack.ClientId,
            "monthly_pack.submitted",
            "Monthly pack submitted",
            $"Monthly pack {pack.Year:D4}-{pack.Month:D2} was submitted for review.",
            $"/monthly-packs/{pack.ClientId}/{pack.Year}/{pack.Month}",
            new { pack.Id, pack.ClientId, pack.Year, pack.Month, pack.Status },
            ct);

        return (false, false, null, pack);
    }

    private static string NormalizeStatus(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "draft" : value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "draft" => "draft",
            "in_progress" => "in_progress",
            "submitted" => "submitted",
            "under_review" => "under_review",
            "completed" => "completed",
            "reopened" => "reopened",
            _ => "draft"
        };
    }
}
