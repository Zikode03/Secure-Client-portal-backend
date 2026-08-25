using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Application.Modules.Platform;
using SecureClientPortal.Backend.Data;

namespace SecureClientPortal.Backend.Infrastructure.Modules.Platform;

/// <summary>
/// Compatibility wrapper around the existing automation engine.
/// The legacy engine still performs reminders, escalations, month-end submission and pack creation,
/// but historically added every active firm template to every client. This wrapper removes only
/// slots created by that automation run and rebuilds those additions from each client's own profile.
/// Existing user-uploaded/current-pack slots are never deleted.
/// </summary>
public sealed class ProfileAwareAutomationWorkflowService : IAutomationWorkflowService
{
    private readonly PortalDbContext _db;
    private readonly IClientMonthlyPackProfileService _profiles;

    public ProfileAwareAutomationWorkflowService(
        PortalDbContext db,
        IClientMonthlyPackProfileService profiles)
    {
        _db = db;
        _profiles = profiles;
    }

    public async Task<AutomationRunSummary> RunAsync(DateTime? utcNow = null, CancellationToken ct = default)
    {
        var now = utcNow?.ToUniversalTime() ?? DateTime.UtcNow;

        // Snapshot slot ids before automation. Anything not in this set afterwards was created by
        // this run, so it is safe to reconcile without touching client uploads or previous work.
        var slotIdsBeforeRun = (await _db.DocumentSlots
            .Select(x => x.Id)
            .ToListAsync(ct))
            .ToHashSet();

        var inner = new AutomationWorkflowService(_db);
        var summary = await inner.RunAsync(now, ct);

        var automationSlots = await _db.DocumentSlots
            .Where(x => !slotIdsBeforeRun.Contains(x.Id))
            .ToListAsync(ct);

        if (automationSlots.Count == 0)
        {
            return summary;
        }

        var affectedPackIds = automationSlots
            .Select(x => x.MonthlyPackId)
            .Distinct()
            .ToList();

        // Remove only the broad legacy-template slots produced by this run.
        _db.DocumentSlots.RemoveRange(automationSlots);
        await _db.SaveChangesAsync(ct);

        var correctedSlotCount = 0;
        foreach (var packId in affectedPackIds)
        {
            var pack = await _db.MonthlyPacks.FirstOrDefaultAsync(x => x.Id == packId, ct);
            if (pack is null)
            {
                continue;
            }

            var beforeCount = await _db.DocumentSlots.CountAsync(x => x.MonthlyPackId == pack.Id, ct);
            await _profiles.ApplyProfileToPackAsync(pack.ClientId, pack.Id, ct);
            var afterCount = await _db.DocumentSlots.CountAsync(x => x.MonthlyPackId == pack.Id, ct);
            correctedSlotCount += Math.Max(0, afterCount - beforeCount);
        }

        // Keep all other automation metrics intact while reporting the corrected slot count.
        return summary with { DocumentSlotsCreated = correctedSlotCount };
    }
}
