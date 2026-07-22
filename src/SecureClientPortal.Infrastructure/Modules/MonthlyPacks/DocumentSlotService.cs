using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application.Contracts.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Application.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Domain.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Domain.Shared.Modules.Documents;
using System.Security.Claims;
using System.Text.Json;

namespace SecureClientPortal.Backend.Infrastructure.Modules.MonthlyPacks;

public sealed class DocumentSlotService : IDocumentSlotService
{
    private readonly PortalDbContext _db;

    public DocumentSlotService(PortalDbContext db)
    {
        _db = db;
    }

    public async Task<(bool forbidden, IReadOnlyList<DocumentSlot>? items)> GetByMonthlyPackIdAsync(string monthlyPackId, ClaimsPrincipal user, CancellationToken ct = default)
    {
        if (!Guid.TryParse(monthlyPackId, out var monthlyPackGuid))
        {
            return (false, null);
        }

        var pack = await _db.MonthlyPacks.FirstOrDefaultAsync(x => x.Id == monthlyPackGuid, ct);
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
            .Where(x => x.MonthlyPackId == monthlyPackGuid)
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

        var normalizedCategory = DocumentDomainValues.NormalizeCategory(request.Category);
        var existing = await _db.DocumentSlots.FirstOrDefaultAsync(x => x.MonthlyPackId == request.MonthlyPackId && x.Category == normalizedCategory, ct);
        if (existing is not null)
        {
            existing.UpdateDefinition(normalizedCategory, request.Label, request.IsRequired);
            existing.UpdateSchedule(request.DueDateUtc);
            await _db.SaveChangesAsync(ct);
            return (false, existing);
        }

        var slot = DocumentSlot.Create(
            Guid.NewGuid(),
            request.MonthlyPackId,
            pack.ClientId,
            normalizedCategory,
            request.Label,
            request.IsRequired,
            request.DueDateUtc,
            DateTime.UtcNow);
        slot.MarkNotStarted();

        _db.DocumentSlots.Add(slot);
        await _db.SaveChangesAsync(ct);
        return (false, slot);
    }

    public async Task<(bool forbidden, bool invalid, string? error, DocumentSlot? slot)> SubmitAsync(string slotId, ClaimsPrincipal user, CancellationToken ct = default)
    {
        if (!Guid.TryParse(slotId, out var documentSlotId))
        {
            return (false, false, null, null);
        }

        var slot = await _db.DocumentSlots.FirstOrDefaultAsync(x => x.Id == documentSlotId, ct);
        if (slot is null)
        {
            return (false, false, null, null);
        }

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(slot.ClientId))
        {
            return (true, false, null, null);
        }

        var document = !slot.CurrentDocumentId.HasValue
            ? null
            : await _db.Documents.FirstOrDefaultAsync(x => x.Id == slot.CurrentDocumentId.Value && x.ClientId == slot.ClientId, ct);
        var hasCurrentVersion = document is not null && await _db.DocumentVersions.AnyAsync(
            x => x.DocumentId == document.Id && x.IsCurrentVersion,
            ct);

        if (document is null || !hasCurrentVersion)
        {
            return (false, true, "The slot must have an uploaded current document version before it can be submitted.", null);
        }

        try
        {
            slot.Submit(
                user.GetUserId() ?? throw new InvalidOperationException("Authenticated user id is required."),
                DateTime.UtcNow);
        }
        catch (DomainRuleException ex)
        {
            return (false, true, ex.Message, null);
        }

        var pack = await _db.MonthlyPacks.FirstOrDefaultAsync(x => x.Id == slot.MonthlyPackId, ct);
        if (pack is not null)
        {
            var slots = await _db.DocumentSlots.Where(x => x.MonthlyPackId == pack.Id).ToListAsync(ct);
            MonthlyPackStatusPolicy.Recalculate(pack, slots);
        }

        await _db.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(
            user,
            "document_slots.submitted",
            "document_slot",
            slot.Id,
            slot.ClientId,
            JsonSerializer.Serialize(new { slot.Id, slot.MonthlyPackId, slot.Status, slot.SubmittedAtUtc, slot.SubmittedByUserId }),
            ct);

        var recipients = await _db.ResolveNotificationRecipientsAsync(slot.ClientId, "accountant", ct);
        await _db.AddNotificationsAsync(
            user,
            recipients,
            slot.ClientId,
            "document_slot.submitted",
            "Document slot submitted",
            $"{slot.Label} was submitted for review.",
            $"/monthly-packs/{slot.ClientId}",
            new { slot.Id, slot.MonthlyPackId, slot.Status, slot.CurrentDocumentId },
            ct);

        return (false, false, null, slot);
    }

    public async Task<(bool forbidden, bool invalid, string? error, DocumentSlot? slot)> MarkNotApplicableAsync(string slotId, ClaimsPrincipal user, CancellationToken ct = default)
    {
        if (!Guid.TryParse(slotId, out var documentSlotId))
        {
            return (false, false, null, null);
        }

        var slot = await _db.DocumentSlots.FirstOrDefaultAsync(x => x.Id == documentSlotId, ct);
        if (slot is null)
        {
            return (false, false, null, null);
        }

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(slot.ClientId))
        {
            return (true, false, null, null);
        }

        if (!user.IsAdmin() && !user.IsAccountant())
        {
            return (true, false, null, null);
        }

        if (slot.Status is "accepted" or "under_review")
        {
            return (false, true, "Accepted or under-review slots cannot be marked not applicable.", null);
        }

        slot.MarkNotApplicable();

        var pack = await _db.MonthlyPacks.FirstOrDefaultAsync(x => x.Id == slot.MonthlyPackId, ct);
        if (pack is not null)
        {
            var slots = await _db.DocumentSlots.Where(x => x.MonthlyPackId == pack.Id).ToListAsync(ct);
            MonthlyPackStatusPolicy.Recalculate(pack, slots);
        }

        await _db.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(
            user,
            "document_slots.marked_not_applicable",
            "document_slot",
            slot.Id,
            slot.ClientId,
            JsonSerializer.Serialize(new { slot.Id, slot.MonthlyPackId, slot.Status }),
            ct);

        return (false, false, null, slot);
    }
}


