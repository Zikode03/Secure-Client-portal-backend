using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application.Modules.Compliance;
using SecureClientPortal.Backend.Application.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Application.Modules.Platform;
using SecureClientPortal.Backend.Data;

namespace SecureClientPortal.Backend.Infrastructure.Modules.Platform;

/// <summary>
/// Compatibility wrapper around the existing automation engine. It corrects legacy broad monthly-pack
/// slot creation and also runs the Phase 4 compliance engine. The optional compliance dependency keeps
/// existing unit tests and direct two-argument construction backwards compatible.
/// </summary>
public sealed class ProfileAwareAutomationWorkflowService : IAutomationWorkflowService
{
    private readonly PortalDbContext _db;
    private readonly IClientMonthlyPackProfileService _profiles;
    private readonly IComplianceAutomationService? _complianceAutomation;

    public ProfileAwareAutomationWorkflowService(
        PortalDbContext db,
        IClientMonthlyPackProfileService profiles,
        IComplianceAutomationService? complianceAutomation = null)
    {
        _db = db;
        _profiles = profiles;
        _complianceAutomation = complianceAutomation;
    }

    public async Task<AutomationRunSummary> RunAsync(DateTime? utcNow = null, CancellationToken ct = default)
    {
        var now = utcNow?.ToUniversalTime() ?? DateTime.UtcNow;

        var packIdsBeforeRun = (await _db.MonthlyPacks.Select(x => x.Id).ToListAsync(ct)).ToHashSet();
        var slotIdsBeforeRun = (await _db.DocumentSlots.Select(x => x.Id).ToListAsync(ct)).ToHashSet();

        var inner = new AutomationWorkflowService(_db);
        var summary = await inner.RunAsync(now, ct);

        // Compliance generation must not depend on whether a monthly pack happened to change today.
        // The service is idempotent, so scheduled runs can safely evaluate every active client.
        if (_complianceAutomation is not null)
        {
            var compliance = await _complianceAutomation.RunSystemAsync(now, ct);
            summary = summary with
            {
                ComplianceItemsUpdated = summary.ComplianceItemsUpdated + compliance.ObligationsCreated + compliance.ObligationsRefreshed
            };
        }

        var automationSlots = await _db.DocumentSlots
            .Where(x => !slotIdsBeforeRun.Contains(x.Id))
            .ToListAsync(ct);
        var newPackIds = (await _db.MonthlyPacks
            .Where(x => !packIdsBeforeRun.Contains(x.Id))
            .Select(x => x.Id)
            .ToListAsync(ct))
            .ToHashSet();

        var affectedPackIds = automationSlots
            .Select(x => x.MonthlyPackId)
            .Concat(newPackIds)
            .Distinct()
            .ToList();

        if (affectedPackIds.Count == 0)
        {
            return summary;
        }

        // Remove only broad slots created by the legacy run, never pre-existing client work.
        if (automationSlots.Count > 0)
        {
            _db.DocumentSlots.RemoveRange(automationSlots);
            await _db.SaveChangesAsync(ct);
        }

        var correctedSlotCount = 0;
        foreach (var packId in affectedPackIds)
        {
            var pack = await _db.MonthlyPacks.FirstOrDefaultAsync(x => x.Id == packId, ct);
            if (pack is null) continue;

            var beforeCount = await _db.DocumentSlots.CountAsync(x => x.MonthlyPackId == pack.Id, ct);
            await _profiles.ApplyProfileToPackAsync(pack.ClientId, pack.Id, ct);
            var afterCount = await _db.DocumentSlots.CountAsync(x => x.MonthlyPackId == pack.Id, ct);
            correctedSlotCount += Math.Max(0, afterCount - beforeCount);
        }

        return summary with { DocumentSlotsCreated = correctedSlotCount };
    }
}
