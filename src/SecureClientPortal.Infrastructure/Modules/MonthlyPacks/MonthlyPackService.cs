using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Application.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Domain.Modules.Documents.Services;
using SecureClientPortal.Backend.Domain.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Domain.Shared.Modules.MonthlyPacks;
using System.Security.Claims;
using System.Text.Json;

namespace SecureClientPortal.Backend.Infrastructure.Modules.MonthlyPacks;

public sealed class MonthlyPackService : IMonthlyPackService
{
    private readonly PortalDbContext _db;
    private readonly IClientMonthlyPackProfileService _profileService;

    public MonthlyPackService(PortalDbContext db, IClientMonthlyPackProfileService profileService)
    {
        _db = db;
        _profileService = profileService;
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

        // A newly created month inherits the client's effective profile immediately.
        // This is where firm defaults and approved client-specific recurring items become real slots.
        await _profileService.ApplyProfileToPackAsync(pack.ClientId, pack.Id, ct);

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

        if (pack.Status is "under_review" or "complete" or "closed")
        {
            return (false, true, "This monthly pack has already been submitted or completed.", null);
        }

        var slots = await _db.DocumentSlots
            .Where(x => x.MonthlyPackId == pack.Id)
            .ToListAsync(ct);
        var incompleteRequiredSlots = slots
            .Where(x => x.IsRequired && x.Status is not ("draft" or "submitted" or "under_review" or "accepted" or "not_applicable"))
            .ToList();
        if (incompleteRequiredSlots.Count > 0)
        {
            return (false, true, "Upload all required documents before submitting the monthly pack.", null);
        }

        var draftSlots = slots.Where(x => x.Status == "draft").ToList();
        var supportingDocuments = await _db.Documents
            .Where(x =>
                x.MonthlyPackId == pack.Id &&
                x.ClientId == pack.ClientId &&
                x.DocumentSlotId == null &&
                x.Status == "uploaded")
            .ToListAsync(ct);

        var alreadySubmittedSlots = slots.Any(x => x.Status is "submitted" or "under_review" or "accepted");
        if (draftSlots.Count == 0 && supportingDocuments.Count == 0 && !alreadySubmittedSlots)
        {
            return (false, true, "This monthly pack has no documents ready to submit.", null);
        }

        var documentIds = draftSlots
            .Where(x => x.CurrentDocumentId.HasValue)
            .Select(x => x.CurrentDocumentId!.Value)
            .Distinct()
            .ToList();
        var documents = await _db.Documents
            .Where(x => documentIds.Contains(x.Id) && x.ClientId == pack.ClientId)
            .ToDictionaryAsync(x => x.Id, ct);
        var versions = await _db.DocumentVersions
            .Where(x => documentIds.Contains(x.DocumentId) && x.IsCurrentVersion)
            .ToDictionaryAsync(x => x.DocumentId, ct);

        if (draftSlots.Any(x =>
                !x.CurrentDocumentId.HasValue ||
                !documents.ContainsKey(x.CurrentDocumentId.Value) ||
                !versions.ContainsKey(x.CurrentDocumentId.Value)))
        {
            return (false, true, "Every draft slot must have an uploaded current document version before the month can be submitted.", null);
        }

        var submittedByUserId = user.GetUserId();
        if (!submittedByUserId.HasValue)
        {
            return (true, false, null, null);
        }

        var submittedAtUtc = DateTime.UtcNow;
        var submissionService = new DocumentSubmissionDomainService();
        try
        {
            foreach (var slot in draftSlots)
            {
                var documentId = slot.CurrentDocumentId!.Value;
                submissionService.Submit(
                    documents[documentId],
                    versions[documentId],
                    slot,
                    submittedByUserId.Value,
                    submittedAtUtc);
            }

            // Slotless supporting documents travel with the month and enter the same accountant review stage.
            foreach (var supportingDocument in supportingDocuments)
            {
                supportingDocument.MarkUnderReview();
            }

            pack.MarkUnderReview();
        }
        catch (DomainRuleException ex)
        {
            return (false, true, ex.Message, null);
        }

        await _db.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(
            user,
            "monthly_packs.submitted",
            "monthly_pack",
            pack.Id,
            pack.ClientId,
            JsonSerializer.Serialize(new
            {
                pack.Id,
                pack.ClientId,
                pack.Year,
                pack.Month,
                pack.Status,
                SubmittedSlotCount = draftSlots.Count,
                SupportingDocumentCount = supportingDocuments.Count
            }),
            ct);

        var recipients = await _db.ResolveNotificationRecipientsAsync(pack.ClientId, "accountant", ct);
        await _db.AddNotificationsAsync(
            user,
            recipients,
            pack.ClientId,
            "monthly_pack.submitted",
            "Monthly pack submitted",
            $"The {new DateTime(pack.Year, pack.Month, 1):MMMM yyyy} monthly pack was submitted for review.",
            $"/monthly-packs/{pack.ClientId}",
            new { pack.Id, pack.Year, pack.Month, pack.Status, SubmittedSlotCount = draftSlots.Count, SupportingDocumentCount = supportingDocuments.Count },
            ct);

        return (false, false, null, pack);
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
        try
        {
            pack.CloseIfReady(slots);
        }
        catch (DomainRuleException ex)
        {
            return (false, true, ex.Message, null);
        }
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
