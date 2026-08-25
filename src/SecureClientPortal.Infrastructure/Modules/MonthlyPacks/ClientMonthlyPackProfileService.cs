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
/// Builds a monthly-pack profile for each client without introducing a second checklist model.
/// The profile itself is JSON in AppSystemSettings; actual monthly work is still represented by
/// DocumentSlot entities so the existing upload/review/completion workflow remains authoritative.
/// </summary>
public sealed class ClientMonthlyPackProfileService : IClientMonthlyPackProfileService
{
    private readonly PortalDbContext _db;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public ClientMonthlyPackProfileService(PortalDbContext db)
    {
        _db = db;
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
                return ServiceResult<ClientMonthlyPackProfileDto>.ErrorResult("The selected monthly-pack template was not found or is inactive.");
            }
        }

        var state = await LoadStateAsync(clientId, ct);
        state.TemplateId = request.TemplateId;
        state.RecurringItems = request.RecurringItems
            .Where(x => !string.IsNullOrWhiteSpace(x.Label) && !string.IsNullOrWhiteSpace(x.Category))
            .Select(x => new RecurringItemState
            {
                Id = Guid.NewGuid(),
                Category = DocumentDomainValues.NormalizeCategory(x.Category),
                Label = x.Label.Trim(),
                IsRequired = x.IsRequired,
                Source = "client_specific"
            })
            .ToList();
        state.UpdatedAtUtc = DateTime.UtcNow;

        await SaveStateAsync(clientId, state, ct);
        await _db.WriteAuditLogAsync(user, "monthly_pack_profile.updated", "client", clientId, clientId,
            JsonSerializer.Serialize(new { state.TemplateId, recurringCount = state.RecurringItems.Count }), ct);

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
            return ServiceResult<AddClientMonthlyPackItemResponse>.ErrorResult("Recurrence must be 'this_month' or 'every_month'.");
        }

        // Add client items only to the latest active pack. Closed/review packs remain immutable.
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

        // DocumentSlot has a unique (pack, category) constraint. A client may add another item whose
        // category already exists, so one-off slots get a harmless unique suffix while retaining the label.
        var slotCategory = category;
        if (await _db.DocumentSlots.AnyAsync(x => x.MonthlyPackId == pack.Id && x.Category == slotCategory, ct))
        {
            slotCategory = $"{category}_client_{Guid.NewGuid():N}"[..Math.Min(80, category.Length + 14)];
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
        pack.MarkInProgress();

        var state = await LoadStateAsync(clientId, ct);
        var actorUserId = user.GetUserId() ?? Guid.Empty;
        var source = user.IsAdmin() || user.IsAccountant() ? "client_specific" : "client_added";
        Guid? recurringRequestId = null;

        // Source metadata is stored outside DocumentSlot so the core domain remains backwards compatible.
        state.OneOffItems.RemoveAll(x => x.SlotId == slot.Id);
        state.OneOffItems.Add(new OneOffItemState { SlotId = slot.Id, Source = source });

        if (recurrence == "every_month")
        {
            if (user.IsAdmin() || user.IsAccountant())
            {
                AddOrReplaceRecurringItem(state, category, label, request.IsRequired);
            }
            else
            {
                recurringRequestId = Guid.NewGuid();
                state.PendingRecurringItems.Add(new PendingRecurringState
                {
                    Id = recurringRequestId.Value,
                    Category = category,
                    Label = label,
                    IsRequired = request.IsRequired,
                    RequestedAtUtc = DateTime.UtcNow,
                    RequestedByUserId = actorUserId
                });
            }
        }

        state.UpdatedAtUtc = DateTime.UtcNow;
        await SaveStateAsync(clientId, state, ct);
        await _db.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(user, "monthly_pack_profile.item_added", "document_slot", slot.Id, clientId,
            JsonSerializer.Serialize(new { slot.Id, pack.Id, recurrence, source, recurringRequestId }), ct);

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

    public async Task ApplyProfileToPackAsync(Guid clientId, Guid monthlyPackId, CancellationToken ct = default)
    {
        var pack = await _db.MonthlyPacks.FirstOrDefaultAsync(x => x.Id == monthlyPackId && x.ClientId == clientId, ct);
        if (pack is null) return;

        var state = await LoadStateAsync(clientId, ct);
        var template = await ResolveTemplateAsync(state.TemplateId, ct);
        var existingCategories = (await _db.DocumentSlots
            .Where(x => x.MonthlyPackId == monthlyPackId)
            .Select(x => x.Category)
            .ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

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
                if (!existingCategories.Add(category)) continue;

                _db.DocumentSlots.Add(DocumentSlot.Create(
                    Guid.NewGuid(), monthlyPackId, clientId, category, requirement.Name, requirement.IsRequired,
                    BuildDueDate(pack.Year, pack.Month, requirement.DefaultDueDayOfMonth), DateTime.UtcNow));
            }
        }

        foreach (var item in state.RecurringItems)
        {
            var category = DocumentDomainValues.NormalizeCategory(item.Category);
            if (!existingCategories.Add(category)) continue;

            _db.DocumentSlots.Add(DocumentSlot.Create(
                Guid.NewGuid(), monthlyPackId, clientId, category, item.Label, item.IsRequired, null, DateTime.UtcNow));
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
            return ServiceResult<ClientMonthlyPackProfileDto>.NotFoundResult("Recurring monthly-pack request was not found.");
        }

        if (approve)
        {
            AddOrReplaceRecurringItem(state, pending.Category, pending.Label, pending.IsRequired);
        }
        state.PendingRecurringItems.RemoveAll(x => x.Id == requestId);
        state.UpdatedAtUtc = DateTime.UtcNow;
        await SaveStateAsync(clientId, state, ct);
        await _db.WriteAuditLogAsync(user,
            approve ? "monthly_pack_profile.recurring_approved" : "monthly_pack_profile.recurring_declined",
            "client", clientId, clientId, JsonSerializer.Serialize(new { requestId, pending.Label }), ct);

        return ServiceResult<ClientMonthlyPackProfileDto>.Success(await BuildDtoAsync(clientId, ct));
    }

    private async Task<ClientMonthlyPackProfileDto> BuildDtoAsync(Guid clientId, CancellationToken ct)
    {
        var state = await LoadStateAsync(clientId, ct);
        var template = await ResolveTemplateAsync(state.TemplateId, ct);
        var recurring = new List<ClientMonthlyPackProfileItemDto>();

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
                x.Id, x.DocumentCategory, x.Name, x.IsRequired, "firm_default")));
        }

        recurring.AddRange(state.RecurringItems.Select(x => new ClientMonthlyPackProfileItemDto(
            x.Id, x.Category, x.Label, x.IsRequired, x.Source)));

        var currentPack = await _db.MonthlyPacks
            .Where(x => x.ClientId == clientId)
            .OrderByDescending(x => x.Year).ThenByDescending(x => x.Month)
            .FirstOrDefaultAsync(ct);
        var currentItems = new List<ClientMonthlyPackCurrentItemDto>();
        if (currentPack is not null)
        {
            var slots = await _db.DocumentSlots.Where(x => x.MonthlyPackId == currentPack.Id).OrderByDescending(x => x.IsRequired).ThenBy(x => x.Label).ToListAsync(ct);
            foreach (var slot in slots)
            {
                var source = state.OneOffItems.FirstOrDefault(x => x.SlotId == slot.Id)?.Source
                    ?? (state.RecurringItems.Any(x => string.Equals(x.Category, slot.Category, StringComparison.OrdinalIgnoreCase))
                        ? "client_specific"
                        : "firm_default");
                currentItems.Add(new ClientMonthlyPackCurrentItemDto(
                    slot.Id, slot.Category, slot.Label, slot.IsRequired, slot.Status, source, slot.DueDateUtc));
            }
        }

        return new ClientMonthlyPackProfileDto(
            clientId,
            template?.Id,
            template?.Name,
            recurring,
            state.PendingRecurringItems.Select(x => new PendingRecurringPackItemDto(
                x.Id, x.Category, x.Label, x.IsRequired, x.RequestedAtUtc, x.RequestedByUserId)).ToList(),
            currentItems,
            state.UpdatedAtUtc);
    }

    private async Task<bool> CanAccessClientAsync(Guid clientId, ClaimsPrincipal user, CancellationToken ct)
    {
        if (user.IsAdmin()) return await _db.Clients.AnyAsync(x => x.Id == clientId, ct);
        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        return allowedClientIds.Contains(clientId);
    }

    private async Task<MonthlyPackTemplate?> ResolveTemplateAsync(Guid? selectedTemplateId, CancellationToken ct)
    {
        if (selectedTemplateId.HasValue)
        {
            var selected = await _db.MonthlyPackTemplates.FirstOrDefaultAsync(x => x.Id == selectedTemplateId.Value && x.IsActive, ct);
            if (selected is not null) return selected;
        }

        // First active template is the firm's fallback when a client has not been explicitly configured.
        return await _db.MonthlyPackTemplates.Where(x => x.IsActive).OrderBy(x => x.CreatedAtUtc).FirstOrDefaultAsync(ct);
    }

    private async Task<ProfileState> LoadStateAsync(Guid clientId, CancellationToken ct)
    {
        var setting = await _db.SystemSettings.FirstOrDefaultAsync(x => x.Key == ProfileKey(clientId), ct);
        if (setting is null) return new ProfileState { UpdatedAtUtc = DateTime.UtcNow };

        try
        {
            return JsonSerializer.Deserialize<ProfileState>(setting.ValueJson, JsonOptions)
                ?? new ProfileState { UpdatedAtUtc = setting.UpdatedAtUtc };
        }
        catch (JsonException)
        {
            // A malformed profile should not break monthly-pack access. Return a clean profile and allow
            // the next successful edit to replace the bad JSON.
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

    private static void AddOrReplaceRecurringItem(ProfileState state, string category, string label, bool isRequired)
    {
        var normalized = DocumentDomainValues.NormalizeCategory(category);
        state.RecurringItems.RemoveAll(x => string.Equals(x.Category, normalized, StringComparison.OrdinalIgnoreCase));
        state.RecurringItems.Add(new RecurringItemState
        {
            Id = Guid.NewGuid(),
            Category = normalized,
            Label = label.Trim(),
            IsRequired = isRequired,
            Source = "client_specific"
        });
    }

    private static DateTime? BuildDueDate(int year, int month, int? dueDay)
    {
        if (!dueDay.HasValue) return null;
        var day = Math.Min(dueDay.Value, DateTime.DaysInMonth(year, month));
        return new DateTime(year, month, day, 23, 59, 59, DateTimeKind.Utc);
    }

    private static string? NormalizeRecurrence(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        return normalized is "this_month" or "every_month" ? normalized : null;
    }

    private static string ProfileKey(Guid clientId) => $"monthly-pack-profile:{clientId:N}";

    private sealed class ProfileState
    {
        public Guid? TemplateId { get; set; }
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
        public string Source { get; set; } = "client_specific";
    }

    private sealed class PendingRecurringState
    {
        public Guid Id { get; set; }
        public string Category { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public bool IsRequired { get; set; }
        public DateTime RequestedAtUtc { get; set; }
        public Guid RequestedByUserId { get; set; }
    }

    private sealed class OneOffItemState
    {
        public Guid SlotId { get; set; }
        public string Source { get; set; } = "client_added";
    }
}
