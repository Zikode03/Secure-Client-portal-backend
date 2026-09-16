using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Application.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Domain.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Domain.Shared.Modules.Documents;
using SecureClientPortal.Backend.Models;
using System.Security.Claims;
using System.Text.Json;

namespace SecureClientPortal.Backend.Infrastructure.Modules.MonthlyPacks;

/// <summary>
/// Builds a monthly-pack profile for each client without creating a competing checklist model.
/// The profile is persisted as JSON in SystemSettings, while actual monthly work remains
/// DocumentSlot data. This keeps the existing upload, completion and review lifecycle intact.
/// </summary>
public sealed class ClientMonthlyPackProfileService : IClientMonthlyPackProfileService
{
    private readonly PortalDbContext _db;
    private readonly BankingDbContext? _bankingDb;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public ClientMonthlyPackProfileService(PortalDbContext db, BankingDbContext? bankingDb = null)
    {
        _db = db;
        _bankingDb = bankingDb;
    }

    public async Task<ServiceResult<ClientMonthlyPackProfileDto>> GetAsync(Guid clientId, ClaimsPrincipal user, CancellationToken ct = default)
    {
        if (!await CanAccessClientAsync(clientId, user, ct))
        {
            return ServiceResult<ClientMonthlyPackProfileDto>.ForbiddenResult();
        }

        return ServiceResult<ClientMonthlyPackProfileDto>.Success(await BuildDtoAsync(clientId, ct));
    }

    public async Task<ServiceResult<ClientMonthlyPackProfileDto>> UpdateAsync(
        Guid clientId,
        UpdateClientMonthlyPackProfileRequest request,
        ClaimsPrincipal user,
        CancellationToken ct = default)
    {
        if (!user.IsAdmin() && !user.IsAccountant())
        {
            return ServiceResult<ClientMonthlyPackProfileDto>.ForbiddenResult();
        }
        if (!await CanAccessClientAsync(clientId, user, ct))
        {
            return ServiceResult<ClientMonthlyPackProfileDto>.ForbiddenResult();
        }

        if (request.TemplateId.HasValue)
        {
            var templateExists = await _db.MonthlyPackTemplates.AnyAsync(
                x => x.Id == request.TemplateId.Value && x.IsActive,
                ct);
            if (!templateExists)
            {
                return ServiceResult<ClientMonthlyPackProfileDto>.ErrorResult(
                    "The selected monthly-pack template was not found or is inactive.");
            }
        }

        var invalidDueDay = request.RecurringItems.FirstOrDefault(x => x.DefaultDueDayOfMonth is < 1 or > 31);
        if (invalidDueDay is not null)
        {
            return ServiceResult<ClientMonthlyPackProfileDto>.ErrorResult(
                $"The recurring due day for '{invalidDueDay.Label}' must be between 1 and 31.");
        }

        var state = await LoadStateAsync(clientId, ct);
        state.TemplateId = request.TemplateId;
        state.RecurringItems = request.RecurringItems
            .Where(x => !string.IsNullOrWhiteSpace(x.Label) && !string.IsNullOrWhiteSpace(x.Category))
            .Select(x =>
            {
                var category = DocumentDomainValues.NormalizeCategory(x.Category);
                var existing = state.RecurringItems.FirstOrDefault(item =>
                    string.Equals(item.Category, category, StringComparison.OrdinalIgnoreCase));
                return new RecurringItemState
                {
                    Id = existing?.Id ?? Guid.NewGuid(),
                    Category = category,
                    Label = x.Label.Trim(),
                    IsRequired = x.IsRequired,
                    DefaultDueDayOfMonth = NormalizeDueDay(x.DefaultDueDayOfMonth),
                    Cadence = NormalizeCadence(x.Cadence),
                    EffectiveFromUtc = NormalizeEffectiveDate(x.EffectiveFromUtc) ?? existing?.EffectiveFromUtc ?? NormalizeEffectiveDate(DateTime.UtcNow)!.Value,
                    EffectiveToUtc = NormalizeEffectiveDate(x.EffectiveToUtc),
                    Source = "client_specific"
                };
            })
            .ToList();

        if (request.OperatingProfile is not null)
        {
            var effectiveFrom = NormalizeEffectiveDate(request.EffectiveFromUtc) ?? NormalizeEffectiveDate(DateTime.UtcNow)!.Value;
            state.OperatingProfiles.RemoveAll(x => x.EffectiveFromUtc == effectiveFrom);
            state.OperatingProfiles.Add(OperatingProfileState.FromInput(request.OperatingProfile, effectiveFrom));
            state.OperatingProfiles = state.OperatingProfiles.OrderBy(x => x.EffectiveFromUtc).ToList();
        }
        state.UpdatedAtUtc = DateTime.UtcNow;

        await SaveStateAsync(clientId, state, ct);
        MonthlyPackReconciliationResultDto? reconciliation = null;
        if (request.ReconcileCurrentPack)
        {
            reconciliation = await ReconcileLatestPackInternalAsync(clientId, state, ct);
        }
        await _db.WriteAuditLogAsync(
            user,
            "monthly_pack_profile.updated",
            "client",
            clientId,
            clientId,
            JsonSerializer.Serialize(new
            {
                state.TemplateId,
                recurringCount = state.RecurringItems.Count,
                operatingProfileVersions = state.OperatingProfiles.Count,
                request.ReconcileCurrentPack,
                reconciliation
            }),
            ct);

        return ServiceResult<ClientMonthlyPackProfileDto>.Success(await BuildDtoAsync(clientId, ct));
    }

    public async Task<ServiceResult<AddClientMonthlyPackItemResponse>> AddItemAsync(
        Guid clientId,
        AddClientMonthlyPackItemRequest request,
        ClaimsPrincipal user,
        CancellationToken ct = default)
    {
        if (!await CanAccessClientAsync(clientId, user, ct))
        {
            return ServiceResult<AddClientMonthlyPackItemResponse>.ForbiddenResult();
        }

        var label = request.Label?.Trim() ?? string.Empty;
        var category = DocumentDomainValues.NormalizeCategory(request.Category ?? string.Empty);
        if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(category))
        {
            return ServiceResult<AddClientMonthlyPackItemResponse>.ErrorResult("Document name and category are required.");
        }

        var recurrence = NormalizeRecurrence(request.Recurrence);
        if (recurrence is null)
        {
            return ServiceResult<AddClientMonthlyPackItemResponse>.ErrorResult(
                "Recurrence must be 'this_month' or 'every_month'.");
        }

        // New client-defined items belong to the latest active month. Once review begins, that month is frozen.
        var pack = await _db.MonthlyPacks
            .Where(x => x.ClientId == clientId)
            .OrderByDescending(x => x.Year)
            .ThenByDescending(x => x.Month)
            .FirstOrDefaultAsync(ct);
        if (pack is null)
        {
            return ServiceResult<AddClientMonthlyPackItemResponse>.ErrorResult("No monthly pack exists for this client yet.");
        }
        if (pack.Status is "under_review" or "complete" or "closed")
        {
            return ServiceResult<AddClientMonthlyPackItemResponse>.ErrorResult(
                "This monthly pack is already with the accountant or completed. Add new requirements to the next pack instead.",
                statusCode: 409);
        }

        // DocumentSlot has a unique (pack, category) constraint. Client-added items may reuse a business
        // category such as invoices, so only the storage category receives a short unique suffix.
        var slotCategory = category;
        if (await _db.DocumentSlots.AnyAsync(x => x.MonthlyPackId == pack.Id && x.Category == slotCategory, ct))
        {
            slotCategory = BuildUniqueSlotCategory(category);
        }

        var slot = DocumentSlot.Create(
            Guid.NewGuid(),
            pack.Id,
            clientId,
            slotCategory,
            label,
            request.IsRequired,
            request.DueDateUtc,
            DateTime.UtcNow);
        slot.MarkNotStarted();
        _db.DocumentSlots.Add(slot);
        if (pack.Status == "not_started")
        {
            pack.MarkInProgress();
        }

        var state = await LoadStateAsync(clientId, ct);
        var actorUserId = user.GetUserId() ?? Guid.Empty;
        var source = user.IsAdmin() || user.IsAccountant() ? "client_specific" : "client_added";
        Guid? recurringRequestId = null;
        var recurringDueDay = recurrence == "every_month" ? request.DueDateUtc?.Day : null;

        // Source metadata lives in the profile store so DocumentSlot remains backwards compatible.
        state.OneOffItems.RemoveAll(x => x.SlotId == slot.Id);
        state.OneOffItems.Add(new OneOffItemState { SlotId = slot.Id, Source = source });

        if (recurrence == "every_month")
        {
            if (user.IsAdmin() || user.IsAccountant())
            {
                // A professional user can approve the recurring rule at the same time they add it.
                AddOrReplaceRecurringItem(state, category, label, request.IsRequired, recurringDueDay);
            }
            else
            {
                // A client may request recurring behavior but cannot silently redefine the firm's future checklist.
                recurringRequestId = Guid.NewGuid();
                state.PendingRecurringItems.Add(new PendingRecurringState
                {
                    Id = recurringRequestId.Value,
                    Category = category,
                    Label = label,
                    IsRequired = request.IsRequired,
                    DefaultDueDayOfMonth = recurringDueDay,
                    RequestedAtUtc = DateTime.UtcNow,
                    RequestedByUserId = actorUserId
                });
            }
        }

        state.UpdatedAtUtc = DateTime.UtcNow;
        await SaveStateAsync(clientId, state, ct);
        await _db.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(
            user,
            "monthly_pack_profile.item_added",
            "document_slot",
            slot.Id,
            clientId,
            JsonSerializer.Serialize(new
            {
                SlotId = slot.Id,
                PackId = pack.Id,
                recurrence,
                source,
                recurringRequestId,
                recurringDueDay
            }),
            ct);

        return ServiceResult<AddClientMonthlyPackItemResponse>.Success(new AddClientMonthlyPackItemResponse(
            slot.Id,
            pack.Id,
            recurringRequestId,
            recurrence,
            source));
    }

    public Task<ServiceResult<ClientMonthlyPackProfileDto>> ApproveRecurringAsync(
        Guid clientId,
        Guid requestId,
        ClaimsPrincipal user,
        CancellationToken ct = default) =>
        ResolveRecurringRequestAsync(clientId, requestId, approve: true, user, ct);

    public Task<ServiceResult<ClientMonthlyPackProfileDto>> DeclineRecurringAsync(
        Guid clientId,
        Guid requestId,
        ClaimsPrincipal user,
        CancellationToken ct = default) =>
        ResolveRecurringRequestAsync(clientId, requestId, approve: false, user, ct);

    public async Task<ServiceResult<MonthlyPackReconciliationResultDto>> ReconcileCurrentPackAsync(
        Guid clientId,
        ClaimsPrincipal user,
        CancellationToken ct = default)
    {
        if (!user.IsAdmin() && !user.IsAccountant())
        {
            return ServiceResult<MonthlyPackReconciliationResultDto>.ForbiddenResult();
        }
        if (!await CanAccessClientAsync(clientId, user, ct))
        {
            return ServiceResult<MonthlyPackReconciliationResultDto>.ForbiddenResult();
        }

        var state = await LoadStateAsync(clientId, ct);
        var result = await ReconcileLatestPackInternalAsync(clientId, state, ct);
        if (result is null)
        {
            return ServiceResult<MonthlyPackReconciliationResultDto>.NotFoundResult(
                "No monthly pack exists for this client.");
        }

        await _db.WriteAuditLogAsync(
            user,
            "monthly_pack_profile.current_pack_reconciled",
            "monthly_pack",
            result.MonthlyPackId,
            clientId,
            JsonSerializer.Serialize(result),
            ct);
        return ServiceResult<MonthlyPackReconciliationResultDto>.Success(result);
    }

    public async Task ApplyProfileToPackAsync(Guid clientId, Guid monthlyPackId, CancellationToken ct = default)
    {
        var pack = await _db.MonthlyPacks.FirstOrDefaultAsync(
            x => x.Id == monthlyPackId && x.ClientId == clientId,
            ct);
        if (pack is null) return;

        var state = await LoadStateAsync(clientId, ct);
        var requirements = await BuildEffectiveRequirementsAsync(state, pack, ct);
        var existingCategories = (await _db.DocumentSlots
            .Where(x => x.MonthlyPackId == monthlyPackId)
            .Select(x => x.Category)
            .ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var requirement in requirements)
        {
            var category = DocumentDomainValues.NormalizeCategory(requirement.Category);
            if (!existingCategories.Add(category)) continue;

            var slot = DocumentSlot.Create(
                Guid.NewGuid(),
                monthlyPackId,
                clientId,
                category,
                requirement.Label,
                requirement.IsRequired,
                BuildDueDate(pack.Year, pack.Month, requirement.DefaultDueDayOfMonth),
                DateTime.UtcNow);
            slot.MarkNotStarted();
            _db.DocumentSlots.Add(slot);
        }

        var connected = await HasActiveBankConnectionAsync(clientId, ct);
        foreach (var entry in _db.ChangeTracker.Entries<DocumentSlot>().Where(x =>
                     x.State != EntityState.Deleted && x.Entity.MonthlyPackId == pack.Id && x.Entity.Category == "bank_statement"))
        {
            var slot = entry.Entity;
            var hasEvidence = slot.CurrentDocumentId.HasValue || slot.Status is not ("not_started" or "not_applicable") ||
                await _db.Documents.AnyAsync(x => x.DocumentSlotId == slot.Id, ct);
            BankStatementSlotPolicy.Apply(slot, connected, hasEvidence);
        }
        await _db.SaveChangesAsync(ct);
    }

    private async Task<ServiceResult<ClientMonthlyPackProfileDto>> ResolveRecurringRequestAsync(
        Guid clientId,
        Guid requestId,
        bool approve,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        if (!user.IsAdmin() && !user.IsAccountant())
        {
            return ServiceResult<ClientMonthlyPackProfileDto>.ForbiddenResult();
        }
        if (!await CanAccessClientAsync(clientId, user, ct))
        {
            return ServiceResult<ClientMonthlyPackProfileDto>.ForbiddenResult();
        }

        var state = await LoadStateAsync(clientId, ct);
        var pending = state.PendingRecurringItems.FirstOrDefault(x => x.Id == requestId);
        if (pending is null)
        {
            return ServiceResult<ClientMonthlyPackProfileDto>.NotFoundResult(
                "Recurring monthly-pack request was not found.");
        }

        if (approve)
        {
            AddOrReplaceRecurringItem(
                state,
                pending.Category,
                pending.Label,
                pending.IsRequired,
                pending.DefaultDueDayOfMonth);
        }
        state.PendingRecurringItems.RemoveAll(x => x.Id == requestId);
        state.UpdatedAtUtc = DateTime.UtcNow;
        await SaveStateAsync(clientId, state, ct);
        await _db.WriteAuditLogAsync(
            user,
            approve ? "monthly_pack_profile.recurring_approved" : "monthly_pack_profile.recurring_declined",
            "client",
            clientId,
            clientId,
            JsonSerializer.Serialize(new { requestId, pending.Label, pending.DefaultDueDayOfMonth }),
            ct);

        return ServiceResult<ClientMonthlyPackProfileDto>.Success(await BuildDtoAsync(clientId, ct));
    }

    private async Task<ClientMonthlyPackProfileDto> BuildDtoAsync(Guid clientId, CancellationToken ct)
    {
        var state = await LoadStateAsync(clientId, ct);
        var template = await ResolveTemplateAsync(state.TemplateId, ct);
        var recurring = new List<ClientMonthlyPackProfileItemDto>();

        var availableTemplates = await _db.MonthlyPackTemplates
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => new ClientMonthlyPackTemplateOptionDto(x.Id, x.Name, x.Description))
            .ToListAsync(ct);

        if (template is not null)
        {
            var templateItems = await
                (from link in _db.MonthlyPackTemplateItems
                 join requirement in _db.RequiredDocumentTemplates on link.RequiredDocumentTemplateId equals requirement.Id
                 where link.MonthlyPackTemplateId == template.Id && requirement.IsActive
                 orderby link.SortOrder
                 select requirement)
                .ToListAsync(ct);
            recurring.AddRange(templateItems.Select(x => new ClientMonthlyPackProfileItemDto(
                x.Id,
                x.DocumentCategory,
                x.Name,
                x.IsRequired,
                "firm_default",
                x.DefaultDueDayOfMonth,
                "monthly",
                null,
                null,
                "Included by the selected firm template.")));
        }

        recurring.AddRange(state.RecurringItems.Select(x => new ClientMonthlyPackProfileItemDto(
            x.Id,
            x.Category,
            x.Label,
            x.IsRequired,
            x.Source,
            x.DefaultDueDayOfMonth,
            x.Cadence,
            x.EffectiveFromUtc,
            x.EffectiveToUtc,
            "Approved client-specific requirement.")));

        var currentPack = await _db.MonthlyPacks
            .Where(x => x.ClientId == clientId)
            .OrderByDescending(x => x.Year)
            .ThenByDescending(x => x.Month)
            .FirstOrDefaultAsync(ct);

        var currentItems = new List<ClientMonthlyPackCurrentItemDto>();
        var profileForPeriod = ResolveOperatingProfile(
            state,
            currentPack?.Year ?? DateTime.UtcNow.Year,
            currentPack?.Month ?? DateTime.UtcNow.Month);
        var recommendations = BuildRecommendations(
            profileForPeriod,
            currentPack?.Year ?? DateTime.UtcNow.Year,
            currentPack?.Month ?? DateTime.UtcNow.Month,
            await HasActiveBankConnectionAsync(clientId, ct));
        var recommendationByCategory = recommendations.ToDictionary(
            x => DocumentDomainValues.NormalizeCategory(x.Category),
            StringComparer.OrdinalIgnoreCase);
        if (currentPack is not null)
        {
            var slots = await _db.DocumentSlots
                .Where(x => x.MonthlyPackId == currentPack.Id)
                .OrderByDescending(x => x.IsRequired)
                .ThenBy(x => x.Label)
                .ToListAsync(ct);

            foreach (var slot in slots)
            {
                var source = state.OneOffItems.FirstOrDefault(x => x.SlotId == slot.Id)?.Source
                    ?? (state.RecurringItems.Any(x => string.Equals(x.Category, slot.Category, StringComparison.OrdinalIgnoreCase))
                        ? "client_specific"
                        : "firm_default");

                currentItems.Add(new ClientMonthlyPackCurrentItemDto(
                    slot.Id,
                    slot.Category,
                    slot.Label,
                    slot.IsRequired,
                    slot.Status,
                    source,
                    slot.DueDateUtc,
                    recommendationByCategory.GetValueOrDefault(slot.Category)?.Reason
                        ?? (source == "client_specific"
                            ? "Approved specifically for this business."
                            : "Included by the selected firm template.")));
            }
        }

        var (recommendedTemplate, recommendedReason) = await RecommendTemplateAsync(clientId, profileForPeriod, ct);

        return new ClientMonthlyPackProfileDto(
            clientId,
            template?.Id,
            template?.Name,
            availableTemplates,
            recurring,
            state.PendingRecurringItems.Select(x => new PendingRecurringPackItemDto(
                x.Id,
                x.Category,
                x.Label,
                x.IsRequired,
                x.RequestedAtUtc,
                x.RequestedByUserId,
                x.DefaultDueDayOfMonth)).ToList(),
            currentItems,
            state.UpdatedAtUtc,
            profileForPeriod?.ToDto(),
            recommendations,
            recommendedTemplate?.Id,
            recommendedReason);
    }

    private async Task<List<EffectiveRequirement>> BuildEffectiveRequirementsAsync(
        ProfileState state,
        MonthlyPack pack,
        CancellationToken ct)
    {
        var template = await ResolveTemplateAsync(state.TemplateId, ct);
        var operatingProfile = ResolveOperatingProfile(state, pack.Year, pack.Month);
        var recommendations = BuildRecommendations(operatingProfile, pack.Year, pack.Month,
            await HasActiveBankConnectionAsync(pack.ClientId, ct));
        var recommendationsByCategory = recommendations.ToDictionary(
            x => DocumentDomainValues.NormalizeCategory(x.Category),
            StringComparer.OrdinalIgnoreCase);
        var effective = new Dictionary<string, EffectiveRequirement>(StringComparer.OrdinalIgnoreCase);

        if (template is not null)
        {
            var templateRequirements = await
                (from link in _db.MonthlyPackTemplateItems
                 join requirement in _db.RequiredDocumentTemplates on link.RequiredDocumentTemplateId equals requirement.Id
                 where link.MonthlyPackTemplateId == template.Id && requirement.IsActive
                 orderby link.SortOrder
                 select requirement)
                .ToListAsync(ct);

            foreach (var requirement in templateRequirements)
            {
                var category = DocumentDomainValues.NormalizeCategory(requirement.DocumentCategory);
                if (recommendationsByCategory.TryGetValue(category, out var recommendation) &&
                    recommendation.Decision is not "include" && category != "bank_statement")
                {
                    continue;
                }

                effective[category] = new EffectiveRequirement(
                    category,
                    requirement.Name,
                    recommendationsByCategory.GetValueOrDefault(category)?.IsRequired ?? requirement.IsRequired,
                    requirement.DefaultDueDayOfMonth,
                    "firm_default",
                    recommendationsByCategory.GetValueOrDefault(category)?.Reason
                        ?? $"Included by the confirmed {template.Name} template.");
            }
        }

        // Operating facts add modules even when the selected starting template did not contain them.
        // Generic bank and invoice requirements remain owned by the single selected template.
        foreach (var recommendation in recommendations.Where(x =>
                     x.Decision == "include" && IsConditionalCategory(x.Category)))
        {
            var category = DocumentDomainValues.NormalizeCategory(recommendation.Category);
            effective.TryAdd(category, new EffectiveRequirement(
                category,
                recommendation.Label,
                recommendation.IsRequired,
                DefaultDueDay(category),
                "business_rule",
                recommendation.Reason));
        }

        foreach (var item in state.RecurringItems.Where(x =>
                     IsEffectiveForPeriod(x.EffectiveFromUtc, x.EffectiveToUtc, pack.Year, pack.Month) &&
                     IsCadenceDue(x.Cadence, x.EffectiveFromUtc, pack.Year, pack.Month)))
        {
            var category = DocumentDomainValues.NormalizeCategory(item.Category);
            effective[category] = new EffectiveRequirement(
                category,
                item.Label,
                item.IsRequired,
                item.DefaultDueDayOfMonth,
                item.Source,
                "Approved specifically for this business.");
        }

        // The Banking slot remains part of the checklist even when no firm template includes it.
        if (_bankingDb is not null)
            effective["bank_statement"] = new EffectiveRequirement("bank_statement", "Bank Statement", true,
                effective.GetValueOrDefault("bank_statement")?.DefaultDueDayOfMonth, "business_rule",
                recommendationsByCategory["bank_statement"].Reason);
        return effective.Values.ToList();
    }

    private async Task<MonthlyPackReconciliationResultDto?> ReconcileLatestPackInternalAsync(
        Guid clientId,
        ProfileState state,
        CancellationToken ct)
    {
        var pack = await _db.MonthlyPacks
            .Where(x => x.ClientId == clientId)
            .OrderByDescending(x => x.Year)
            .ThenByDescending(x => x.Month)
            .FirstOrDefaultAsync(ct);
        if (pack is null) return null;

        // Reviewed and completed packs are historical evidence and are never rewritten.
        if (pack.Status is "under_review" or "complete" or "closed")
        {
            return new MonthlyPackReconciliationResultDto(pack.Id, 0, 0, 0, 0);
        }

        var expected = (await BuildEffectiveRequirementsAsync(state, pack, ct))
            .ToDictionary(x => x.Category, StringComparer.OrdinalIgnoreCase);
        var existing = await _db.DocumentSlots
            .Where(x => x.MonthlyPackId == pack.Id)
            .ToListAsync(ct);
        var linkedSlotIds = (await _db.Documents
            .Where(x => x.MonthlyPackId == pack.Id && x.DocumentSlotId.HasValue)
            .Select(x => x.DocumentSlotId!.Value)
            .ToListAsync(ct))
            .ToHashSet();

        var added = 0;
        var removed = 0;
        var preserved = 0;
        var updated = 0;

        foreach (var slot in existing)
        {
            if (expected.Remove(slot.Category, out var requirement))
            {
                if (slot.Label != requirement.Label || slot.IsRequired != requirement.IsRequired)
                {
                    slot.UpdateDefinition(slot.Category, requirement.Label, requirement.IsRequired);
                    updated++;
                }
                slot.UpdateSchedule(BuildDueDate(pack.Year, pack.Month, requirement.DefaultDueDayOfMonth));
                continue;
            }

            var hasEvidence = slot.CurrentDocumentId.HasValue || linkedSlotIds.Contains(slot.Id) || slot.Status != "not_started";
            if (hasEvidence)
            {
                // Evidence stays visible for audit history, but an irrelevant legacy slot cannot block submission.
                if (slot.IsRequired)
                {
                    slot.UpdateDefinition(slot.Category, slot.Label, false);
                    updated++;
                }
                state.OneOffItems.RemoveAll(x => x.SlotId == slot.Id);
                state.OneOffItems.Add(new OneOffItemState { SlotId = slot.Id, Source = "preserved_evidence" });
                preserved++;
            }
            else
            {
                _db.DocumentSlots.Remove(slot);
                state.OneOffItems.RemoveAll(x => x.SlotId == slot.Id);
                removed++;
            }
        }

        foreach (var requirement in expected.Values)
        {
            var slot = DocumentSlot.Create(
                Guid.NewGuid(),
                pack.Id,
                clientId,
                requirement.Category,
                requirement.Label,
                requirement.IsRequired,
                BuildDueDate(pack.Year, pack.Month, requirement.DefaultDueDayOfMonth),
                DateTime.UtcNow);
            slot.MarkNotStarted();
            _db.DocumentSlots.Add(slot);
            added++;
        }

        var connected = await HasActiveBankConnectionAsync(clientId, ct);
        foreach (var entry in _db.ChangeTracker.Entries<DocumentSlot>().Where(x =>
                     x.State != EntityState.Deleted && x.Entity.MonthlyPackId == pack.Id && x.Entity.Category == "bank_statement"))
        {
            var slot = entry.Entity;
            BankStatementSlotPolicy.Apply(slot, connected,
                slot.CurrentDocumentId.HasValue || linkedSlotIds.Contains(slot.Id) ||
                slot.Status is not ("not_started" or "not_applicable"));
        }
        state.UpdatedAtUtc = DateTime.UtcNow;
        await SaveStateAsync(clientId, state, ct);
        await _db.SaveChangesAsync(ct);
        return new MonthlyPackReconciliationResultDto(pack.Id, added, removed, preserved, updated);
    }

    private static List<MonthlyPackRequirementRecommendationDto> BuildRecommendations(
        OperatingProfileState? profile,
        int year,
        int month,
        bool hasActiveBankConnection)
    {
        var items = new List<MonthlyPackRequirementRecommendationDto>();

        items.Add(hasActiveBankConnection
            ? Recommendation("bank_statement", "Bank Statement", true, "monthly", "connected", "Bank information is supplied by the connected bank feed; no upload is required.")
            : Recommendation("bank_statement", "Bank Statement", true, "monthly", "include", "Required for each active business bank account when no bank feed supplies it."));
        items.Add(profile?.SalesInvoicesSynced == true && profile.PurchaseInvoicesSynced
            ? Recommendation("invoices", "Invoices", true, "monthly", "connected", "Sales and purchase invoices are supplied by the accounting integration.")
            : Recommendation("invoices", "Invoices", true, "monthly", "include", "Invoice support is still collected manually for this business."));
        items.Add(profile?.SalesInvoicesSynced == true
            ? Recommendation("sales_invoices", "Sales Invoices", true, "monthly", "connected", "Sales invoices are supplied by the accounting integration.")
            : Recommendation("sales_invoices", "Sales Invoices", true, "monthly", "include", "Sales invoices are not supplied by an integration."));
        items.Add(profile?.PurchaseInvoicesSynced == true
            ? Recommendation("purchase_invoices", "Purchase Invoices", true, "monthly", "connected", "Purchase invoices are supplied by the accounting integration.")
            : Recommendation("purchase_invoices", "Purchase Invoices", true, "monthly", "include", "Purchase invoices are not supplied by an integration."));

        AddConditional(items, profile?.HasEmployees, "payroll_document", "Payroll / PAYE", true, "monthly", "The business has employees and runs payroll.");
        AddConditional(items, profile?.HoldsInventory, "inventory_report", "Stock / Inventory Report", true, "monthly", "The business holds or sells stock.");
        AddConditional(items, profile?.UsesSupplierAccounts, "supplier_statements", "Supplier Statements", false, "monthly", "The business buys on supplier accounts that require reconciliation.");
        AddConditional(items, profile?.UsesPos, "merchant_statement", "POS / Merchant Statements", true, "monthly", "The business accepts card or point-of-sale settlements.");
        AddConditional(items, profile?.OperatesFleet, "fuel_statement", "Fuel Statements", true, "monthly", "The business operates vehicles or a fleet.");
        AddConditional(items, profile?.OperatesFleet, "vehicle_finance", "Vehicle Finance Statements", false, "monthly", "The business operates financed or leased vehicles.");
        AddConditional(items, profile?.OperatesFleet, "toll_tracking", "Toll / Tracking Statements", false, "monthly", "The business operates vehicles or a fleet.");
        AddConditional(items, profile?.UsesSubcontractors, "subcontractor_invoices", "Subcontractor Invoices", true, "monthly", "The business uses subcontractors.");
        AddConditional(items, profile?.UsesPaymentCertificates, "payment_certificates", "Payment Certificates", true, "monthly", "The business bills or pays through project payment certificates.");
        AddConditional(items, profile?.TracksProjectCosts, "project_expenses", "Project Expense Support", false, "monthly", "The business tracks expenditure by project.");
        AddConditional(items, profile?.UsesBookingPlatforms, "booking_statement", "Booking / Platform Statements", false, "monthly", "The business receives settlements from booking or marketplace platforms.");
        AddConditional(items, profile?.UsesFoodSuppliers, "food_supplier_statement", "Food & Beverage Supplier Statements", false, "monthly", "The business uses food and beverage suppliers.");
        AddConditional(items, profile?.ManufacturesGoods, "production_report", "Production Report", false, "monthly", "The business manufactures or produces goods.");

        if (profile?.VatRegistered is null)
        {
            items.Add(Recommendation("tax_document", "VAT / Tax Documents", true, "vat_cycle", "needs_answer", "Confirm whether the business is VAT registered."));
        }
        else if (!profile.VatRegistered.Value)
        {
            items.Add(Recommendation("tax_document", "VAT / Tax Documents", true, "vat_cycle", "not_applicable", "The business is not VAT registered."));
        }
        else if (IsVatPeriodDue(profile, year, month))
        {
            items.Add(Recommendation("tax_document", "VAT / Tax Documents", true, $"every_{profile.VatCycleMonths}_months", "include", "The configured VAT filing period is due this month."));
        }
        else
        {
            items.Add(Recommendation("tax_document", "VAT / Tax Documents", true, $"every_{profile.VatCycleMonths}_months", "not_due", "The business is VAT registered, but its configured VAT period is not due this month."));
        }

        return items;
    }

    private static void AddConditional(
        ICollection<MonthlyPackRequirementRecommendationDto> items,
        bool? answer,
        string category,
        string label,
        bool required,
        string cadence,
        string includedReason)
    {
        items.Add(answer switch
        {
            true => Recommendation(category, label, required, cadence, "include", includedReason),
            false => Recommendation(category, label, required, cadence, "not_applicable", "The confirmed operating profile says this does not apply."),
            _ => Recommendation(category, label, required, cadence, "needs_answer", $"Confirm whether '{label}' applies to this business.")
        });
    }

    private static MonthlyPackRequirementRecommendationDto Recommendation(
        string category,
        string label,
        bool required,
        string cadence,
        string decision,
        string reason) =>
        new(category, label, required, cadence, decision, reason);

    private async Task<(MonthlyPackTemplate? Template, string? Reason)> RecommendTemplateAsync(
        Guid clientId,
        OperatingProfileState? profile,
        CancellationToken ct)
    {
        var client = await _db.Clients.FirstOrDefaultAsync(x => x.Id == clientId, ct);
        if (client is null) return (null, null);

        string? keyword = null;
        string? reason = null;
        // Industry describes business activity. EntityType is the legal form only and must never
        // be used as a substitute because that can recommend the wrong monthly-pack template.
        var industryText = client.Industry?.Trim().ToLowerInvariant() ?? string.Empty;

        if (profile?.ManufacturesGoods == true || industryText.Contains("manufactur") || industryText.Contains("production"))
        {
            keyword = "Manufacturing";
            reason = "Recommended because the business manufactures or produces goods.";
        }
        else if (profile?.UsesBookingPlatforms == true || profile?.UsesFoodSuppliers == true ||
                 industryText.Contains("hospitality") || industryText.Contains("hotel") ||
                 industryText.Contains("restaurant") || industryText.Contains("catering"))
        {
            keyword = "Hospitality";
            reason = "Recommended because the recorded industry or operating profile is hospitality or food service.";
        }
        else if (profile?.UsesSubcontractors == true || profile?.UsesPaymentCertificates == true ||
                 profile?.TracksProjectCosts == true || industryText.Contains("construct") ||
                 industryText.Contains("contractor") || industryText.Contains("engineering"))
        {
            keyword = "Construction";
            reason = "Recommended because the business uses construction, project, contractor, or payment-certificate workflows.";
        }
        else if (profile?.OperatesFleet == true || industryText.Contains("transport") ||
                 industryText.Contains("logistic") || industryText.Contains("fleet") ||
                 industryText.Contains("freight") || industryText.Contains("courier"))
        {
            keyword = "Transport";
            reason = "Recommended because the recorded industry or operating profile involves transport, logistics, or a fleet.";
        }
        else if (profile?.UsesPos == true || profile?.HoldsInventory == true ||
                 industryText.Contains("retail") || industryText.Contains("trading") ||
                 industryText.Contains("wholesale") || industryText.Contains("ecommerce"))
        {
            keyword = "Retail";
            reason = "Recommended because the recorded industry or operating profile involves retail, trading, POS, or inventory.";
        }
        else if (industryText.Contains("professional") || industryText.Contains("consult") ||
                 industryText.Contains("account") || industryText.Contains("legal") ||
                 industryText.Contains("technology") || industryText.Contains("software") ||
                 industryText.Contains("service"))
        {
            keyword = "Professional";
            reason = "Recommended because the recorded industry is a professional or service business.";
        }

        // A missing/unknown industry is not evidence of a professional-services business.
        // Leave the recommendation empty until industry or operating facts are confirmed.
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return (null, null);
        }

        var template = await _db.MonthlyPackTemplates
            .Where(x => x.IsActive && x.Name.Contains(keyword))
            .OrderBy(x => x.Name)
            .FirstOrDefaultAsync(ct);
        return (template, template is null ? null : reason);
    }

    private static OperatingProfileState? ResolveOperatingProfile(ProfileState state, int year, int month)
    {
        var period = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        return state.OperatingProfiles
            .Where(x => x.EffectiveFromUtc <= period)
            .OrderByDescending(x => x.EffectiveFromUtc)
            .FirstOrDefault();
    }

    private static bool IsConditionalCategory(string category) =>
        DocumentDomainValues.NormalizeCategory(category) is
            "payroll_document" or "inventory_report" or "supplier_statements" or
            "merchant_statement" or "fuel_statement" or "vehicle_finance" or "toll_tracking" or
            "subcontractor_invoices" or "payment_certificates" or "project_expenses" or
            "booking_statement" or "food_supplier_statement" or "production_report" or "tax_document";

    private static int? DefaultDueDay(string category) =>
        DocumentDomainValues.NormalizeCategory(category) is
            "bank_statement" or "sales_invoices" or "purchase_invoices" or "merchant_statement" or "fuel_statement" ? 5 : 7;

    private static bool IsVatPeriodDue(OperatingProfileState profile, int year, int month)
    {
        var cycle = Math.Clamp(profile.VatCycleMonths, 1, 12);
        var anchor = Math.Clamp(profile.VatAnchorMonth, 1, 12);
        var absoluteMonth = year * 12 + month - 1;
        var anchorAbsoluteMonth = year * 12 + anchor - 1;
        return Math.Abs(absoluteMonth - anchorAbsoluteMonth) % cycle == 0;
    }

    private static bool IsEffectiveForPeriod(DateTime effectiveFrom, DateTime? effectiveTo, int year, int month)
    {
        var period = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        return effectiveFrom <= period && (!effectiveTo.HasValue || effectiveTo.Value >= period);
    }

    private static bool IsCadenceDue(string cadence, DateTime effectiveFrom, int year, int month)
    {
        var normalized = NormalizeCadence(cadence);
        if (normalized == "event_based") return false;
        var interval = normalized switch { "quarterly" => 3, "annual" => 12, _ => 1 };
        var periodIndex = year * 12 + month;
        var startIndex = effectiveFrom.Year * 12 + effectiveFrom.Month;
        return periodIndex >= startIndex && (periodIndex - startIndex) % interval == 0;
    }

    private sealed record EffectiveRequirement(
        string Category,
        string Label,
        bool IsRequired,
        int? DefaultDueDayOfMonth,
        string Source,
        string Reason);

    private async Task<bool> CanAccessClientAsync(Guid clientId, ClaimsPrincipal user, CancellationToken ct)
    {
        if (user.IsAdmin())
        {
            return await _db.Clients.AnyAsync(x => x.Id == clientId, ct);
        }

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        return allowedClientIds.Contains(clientId);
    }

    private Task<bool> HasActiveBankConnectionAsync(Guid clientId, CancellationToken ct) =>
        _bankingDb is null ? Task.FromResult(false) :
        _bankingDb.BankConnections.AnyAsync(x => x.ClientId == clientId && x.Status != "disconnected", ct);

    private async Task<MonthlyPackTemplate?> ResolveTemplateAsync(Guid? selectedTemplateId, CancellationToken ct)
    {
        if (selectedTemplateId.HasValue)
        {
            var selected = await _db.MonthlyPackTemplates.FirstOrDefaultAsync(
                x => x.Id == selectedTemplateId.Value && x.IsActive,
                ct);
            if (selected is not null) return selected;
        }

        // The oldest active template acts as the firm's safe fallback until a client is explicitly configured.
        return await _db.MonthlyPackTemplates
            .Where(x => x.IsActive)
            .OrderBy(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);
    }

    private async Task<ProfileState> LoadStateAsync(Guid clientId, CancellationToken ct)
    {
        var setting = await _db.SystemSettings.FirstOrDefaultAsync(x => x.Key == ProfileKey(clientId), ct);
        if (setting is null)
        {
            return new ProfileState { UpdatedAtUtc = DateTime.UtcNow };
        }

        try
        {
            return JsonSerializer.Deserialize<ProfileState>(setting.ValueJson, JsonOptions)
                ?? new ProfileState { UpdatedAtUtc = setting.UpdatedAtUtc };
        }
        catch (JsonException)
        {
            // Bad JSON should never prevent a client from opening their pack. A later successful edit
            // will replace the malformed profile with a valid state.
            return new ProfileState { UpdatedAtUtc = setting.UpdatedAtUtc };
        }
    }

    private async Task SaveStateAsync(Guid clientId, ProfileState state, CancellationToken ct)
    {
        var key = ProfileKey(clientId);
        var json = JsonSerializer.Serialize(state, JsonOptions);
        var setting = await _db.SystemSettings.FirstOrDefaultAsync(x => x.Key == key, ct);
        if (setting is null)
        {
            _db.SystemSettings.Add(SystemSetting.Create(key, json));
        }
        else
        {
            setting.UpdateValue(json);
        }

        await _db.SaveChangesAsync(ct);
    }

    private static void AddOrReplaceRecurringItem(
        ProfileState state,
        string category,
        string label,
        bool isRequired,
        int? defaultDueDayOfMonth)
    {
        var normalized = DocumentDomainValues.NormalizeCategory(category);
        state.RecurringItems.RemoveAll(x => string.Equals(x.Category, normalized, StringComparison.OrdinalIgnoreCase));
        state.RecurringItems.Add(new RecurringItemState
        {
            Id = Guid.NewGuid(),
            Category = normalized,
            Label = label.Trim(),
            IsRequired = isRequired,
            DefaultDueDayOfMonth = NormalizeDueDay(defaultDueDayOfMonth),
            Cadence = "monthly",
            EffectiveFromUtc = NormalizeEffectiveDate(DateTime.UtcNow)!.Value,
            Source = "client_specific"
        });
    }

    private static DateTime? BuildDueDate(int year, int month, int? dueDay)
    {
        if (!dueDay.HasValue) return null;
        var day = Math.Min(dueDay.Value, DateTime.DaysInMonth(year, month));
        return new DateTime(year, month, day, 23, 59, 59, DateTimeKind.Utc);
    }

    private static int? NormalizeDueDay(int? dueDay)
    {
        if (!dueDay.HasValue) return null;
        return Math.Clamp(dueDay.Value, 1, 31);
    }

    private static DateTime? NormalizeEffectiveDate(DateTime? value)
    {
        if (!value.HasValue) return null;
        var utc = value.Value.Kind == DateTimeKind.Utc ? value.Value : value.Value.ToUniversalTime();
        return new DateTime(utc.Year, utc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
    }

    private static string NormalizeCadence(string? cadence) =>
        cadence?.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_') switch
        {
            "quarterly" => "quarterly",
            "annual" or "annually" or "yearly" => "annual",
            "event" or "event_based" => "event_based",
            _ => "monthly"
        };

    private static string BuildUniqueSlotCategory(string category)
    {
        var suffix = $"_client_{Guid.NewGuid():N}"[..15];
        var baseLength = Math.Max(1, 80 - suffix.Length);
        var safeBase = category.Length > baseLength ? category[..baseLength] : category;
        return safeBase + suffix;
    }

    private static string? NormalizeRecurrence(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        return normalized is "this_month" or "every_month" ? normalized : null;
    }

    private static string ProfileKey(Guid clientId) => $"monthly-pack-profile:{clientId:N}";

    // These classes are persistence shapes only. They stay private so JSON storage does not leak
    // into the public API contract.
    private sealed class ProfileState
    {
        public Guid? TemplateId { get; set; }
        public List<OperatingProfileState> OperatingProfiles { get; set; } = [];
        public List<RecurringItemState> RecurringItems { get; set; } = [];
        public List<PendingRecurringState> PendingRecurringItems { get; set; } = [];
        public List<OneOffItemState> OneOffItems { get; set; } = [];
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    }

    private sealed class RecurringItemState
    {
        public Guid Id { get; set; }
        public string Category { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public bool IsRequired { get; set; }
        public int? DefaultDueDayOfMonth { get; set; }
        public string Cadence { get; set; } = "monthly";
        public DateTime EffectiveFromUtc { get; set; } = DateTime.MinValue;
        public DateTime? EffectiveToUtc { get; set; }
        public string Source { get; set; } = "client_specific";
    }

    private sealed class OperatingProfileState
    {
        public DateTime EffectiveFromUtc { get; set; }
        public bool? VatRegistered { get; set; }
        public int VatCycleMonths { get; set; } = 2;
        public int VatAnchorMonth { get; set; } = 1;
        public bool? HasEmployees { get; set; }
        public bool? HoldsInventory { get; set; }
        public bool? UsesSupplierAccounts { get; set; }
        public bool? UsesPos { get; set; }
        public bool? OperatesFleet { get; set; }
        public bool? UsesSubcontractors { get; set; }
        public bool? UsesPaymentCertificates { get; set; }
        public bool? TracksProjectCosts { get; set; }
        public bool? UsesBookingPlatforms { get; set; }
        public bool? UsesFoodSuppliers { get; set; }
        public bool? ManufacturesGoods { get; set; }
        public bool BankFeedConnected { get; set; }
        public bool SalesInvoicesSynced { get; set; }
        public bool PurchaseInvoicesSynced { get; set; }

        public static OperatingProfileState FromInput(ClientOperatingProfileInput input, DateTime effectiveFromUtc) =>
            new()
            {
                EffectiveFromUtc = effectiveFromUtc,
                VatRegistered = input.VatRegistered,
                VatCycleMonths = Math.Clamp(input.VatCycleMonths, 1, 12),
                VatAnchorMonth = Math.Clamp(input.VatAnchorMonth, 1, 12),
                HasEmployees = input.HasEmployees,
                HoldsInventory = input.HoldsInventory,
                UsesSupplierAccounts = input.UsesSupplierAccounts,
                UsesPos = input.UsesPos,
                OperatesFleet = input.OperatesFleet,
                UsesSubcontractors = input.UsesSubcontractors,
                UsesPaymentCertificates = input.UsesPaymentCertificates,
                TracksProjectCosts = input.TracksProjectCosts,
                UsesBookingPlatforms = input.UsesBookingPlatforms,
                UsesFoodSuppliers = input.UsesFoodSuppliers,
                ManufacturesGoods = input.ManufacturesGoods,
                BankFeedConnected = input.BankFeedConnected,
                SalesInvoicesSynced = input.SalesInvoicesSynced,
                PurchaseInvoicesSynced = input.PurchaseInvoicesSynced
            };

        public ClientOperatingProfileDto ToDto() =>
            new(
                EffectiveFromUtc,
                VatRegistered,
                VatCycleMonths,
                VatAnchorMonth,
                HasEmployees,
                HoldsInventory,
                UsesSupplierAccounts,
                UsesPos,
                OperatesFleet,
                UsesSubcontractors,
                UsesPaymentCertificates,
                TracksProjectCosts,
                UsesBookingPlatforms,
                UsesFoodSuppliers,
                ManufacturesGoods,
                BankFeedConnected,
                SalesInvoicesSynced,
                PurchaseInvoicesSynced,
                IsComplete());

        private bool IsComplete() =>
            VatRegistered.HasValue && HasEmployees.HasValue && HoldsInventory.HasValue &&
            UsesSupplierAccounts.HasValue && UsesPos.HasValue && OperatesFleet.HasValue &&
            UsesSubcontractors.HasValue && UsesPaymentCertificates.HasValue &&
            TracksProjectCosts.HasValue && UsesBookingPlatforms.HasValue &&
            UsesFoodSuppliers.HasValue && ManufacturesGoods.HasValue;
    }

    private sealed class PendingRecurringState
    {
        public Guid Id { get; set; }
        public string Category { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public bool IsRequired { get; set; }
        public int? DefaultDueDayOfMonth { get; set; }
        public DateTime RequestedAtUtc { get; set; }
        public Guid RequestedByUserId { get; set; }
    }

    private sealed class OneOffItemState
    {
        public Guid SlotId { get; set; }
        public string Source { get; set; } = "client_added";
    }
}
