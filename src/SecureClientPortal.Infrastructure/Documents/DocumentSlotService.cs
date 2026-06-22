using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application.Contracts;
using SecureClientPortal.Backend.Application.Documents;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Models;
using System.Security.Claims;

namespace SecureClientPortal.Backend.Infrastructure.Documents;

public sealed class DocumentSlotService : IDocumentSlotService
{
    private readonly PortalDbContext _db;

    public DocumentSlotService(PortalDbContext db)
    {
        _db = db;
    }

    public async Task<(bool forbidden, IReadOnlyList<DocumentSlot>? items)> GetByMonthlyPackIdAsync(string monthlyPackId, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var pack = await _db.MonthlyPacks.FirstOrDefaultAsync(x => x.Id == monthlyPackId, ct);
        if (pack is null)
        {
            return (false, null);
        }

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(pack.ClientId))
        {
            return (true, null);
        }

        var slots = await _db.DocumentSlots
            .Where(x => x.MonthlyPackId == monthlyPackId)
            .OrderByDescending(x => x.IsRequired)
            .ThenBy(x => x.Label)
            .ToListAsync(ct);

        return (false, slots);
    }

    public async Task<(bool forbidden, DocumentSlot created)> CreateAsync(CreateDocumentSlotRequest request, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var pack = await _db.MonthlyPacks.FirstOrDefaultAsync(x => x.Id == request.MonthlyPackId, ct);
        if (pack is null)
        {
            throw new ArgumentException("Monthly pack was not found.");
        }

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (!user.IsAdmin() && !allowedClientIds.Contains(pack.ClientId))
        {
            return (true, null!);
        }

        var normalizedCategory = NormalizeCategory(request.Category);
        var existing = await _db.DocumentSlots.FirstOrDefaultAsync(x => x.MonthlyPackId == request.MonthlyPackId && x.Category == normalizedCategory, ct);
        if (existing is not null)
        {
            existing.Label = request.Label.Trim();
            existing.IsRequired = request.IsRequired;
            existing.DueDateUtc = request.DueDateUtc;
            existing.UpdatedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return (false, existing);
        }

        var slot = new DocumentSlot
        {
            Id = $"slot_{Guid.NewGuid():N}",
            MonthlyPackId = request.MonthlyPackId,
            ClientId = pack.ClientId,
            Category = normalizedCategory,
            Label = request.Label.Trim(),
            IsRequired = request.IsRequired,
            Status = "missing",
            DueDateUtc = request.DueDateUtc,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _db.DocumentSlots.Add(slot);
        await _db.SaveChangesAsync(ct);
        return (false, slot);
    }

    private static string NormalizeCategory(string value)
    {
        return value.Trim().ToLowerInvariant().Replace("-", "_").Replace(" ", "_");
    }
}
