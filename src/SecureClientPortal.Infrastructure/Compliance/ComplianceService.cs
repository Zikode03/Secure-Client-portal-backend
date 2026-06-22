using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Compliance;
using SecureClientPortal.Backend.Application.Contracts;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Models;
using System.Text.Json;

namespace SecureClientPortal.Backend.Infrastructure.Compliance.Application;

public sealed class ComplianceService : IComplianceService
{
    private static readonly HashSet<string> AllowedItemStatuses = ["missing", "pending", "valid", "expiring_soon", "expired", "rejected"];
    private static readonly HashSet<string> AllowedReminderStatuses = ["pending", "sent", "dismissed"];
    private static readonly HashSet<string> AllowedRiskLevels = ["low", "medium", "high", "critical"];

    private readonly PortalDbContext _db;

    public ComplianceService(PortalDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<ComplianceCategory>> GetCategoriesAsync(CancellationToken ct = default)
    {
        return await _db.ComplianceCategories.OrderBy(x => x.Name).ToListAsync(ct);
    }

    public async Task<ServiceResult<ComplianceCategory>> CreateCategoryAsync(CreateComplianceCategoryRequest request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default)
    {
        ComplianceValidators.ValidateCategory(request);

        var normalizedCode = string.IsNullOrWhiteSpace(request.Code)
            ? GenerateCategoryCode(request.Name)
            : request.Code.Trim().ToUpperInvariant();

        if (await _db.ComplianceCategories.AnyAsync(x => x.Code == normalizedCode, ct))
        {
            return ServiceResult<ComplianceCategory>.ErrorResult("Compliance category code already exists.", statusCode: StatusCodes.Status409Conflict);
        }

        var item = new ComplianceCategory
        {
            Id = $"cc_{Guid.NewGuid():N}",
            Name = request.Name.Trim(),
            Description = request.Description.Trim(),
            Code = normalizedCode,
            IsActive = request.IsActive,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _db.ComplianceCategories.Add(item);
        await _db.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(user, "compliance.category_created", "compliance_category", item.Id, null, JsonSerializer.Serialize(new { item.Name, item.Code }), ct);
        return ServiceResult<ComplianceCategory>.Success(item);
    }

    public async Task<object> SeedDefaultCategoriesAsync(System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default)
    {
        var defaults = new[]
        {
            new ComplianceCategory { Id = "cc_tax_compliance", Name = "Tax Compliance", Code = "TAX", Description = "Income tax, VAT, and tax authority filing obligations.", IsActive = true },
            new ComplianceCategory { Id = "cc_cipc_compliance", Name = "CIPC Compliance", Code = "CIPC", Description = "Company registration, annual returns, and beneficial ownership obligations.", IsActive = true },
            new ComplianceCategory { Id = "cc_payroll_compliance", Name = "Payroll Compliance", Code = "PAYROLL", Description = "Payroll submissions, UIF, PAYE, and employee records.", IsActive = true },
            new ComplianceCategory { Id = "cc_popia_compliance", Name = "POPIA Compliance", Code = "POPIA", Description = "Privacy controls, information processing, and consent evidence.", IsActive = true }
        };

        foreach (var category in defaults)
        {
            var existing = await _db.ComplianceCategories.FirstOrDefaultAsync(x => x.Id == category.Id || x.Code == category.Code, ct);
            if (existing is null)
            {
                category.CreatedAtUtc = DateTime.UtcNow;
                category.UpdatedAtUtc = DateTime.UtcNow;
                _db.ComplianceCategories.Add(category);
            }
            else
            {
                existing.Name = category.Name;
                existing.Code = category.Code;
                existing.Description = category.Description;
                existing.IsActive = true;
                existing.UpdatedAtUtc = DateTime.UtcNow;
            }
        }

        await _db.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(user, "compliance.categories_seeded", "compliance_category", "defaults", null, null, ct);
        return new { seeded = defaults.Length };
    }

    public async Task<ServiceResult<IReadOnlyList<object>>> GetItemsAsync(System.Security.Claims.ClaimsPrincipal user, string? clientId = null, CancellationToken ct = default)
    {
        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        var query = _db.ComplianceItems.Where(x => allowedClientIds.Contains(x.ClientId));

        if (!string.IsNullOrWhiteSpace(clientId))
        {
            if (!allowedClientIds.Contains(clientId))
            {
                return ServiceResult<IReadOnlyList<object>>.ForbiddenResult();
            }

            query = query.Where(x => x.ClientId == clientId);
        }

        var categories = await _db.ComplianceCategories.ToDictionaryAsync(x => x.Id, ct);
        var users = await _db.Users.ToDictionaryAsync(x => x.Id, ct);
        var items = await query.OrderBy(x => x.ClientId).ThenBy(x => x.Name).ToListAsync(ct);

        return ServiceResult<IReadOnlyList<object>>.Success(items.Select(item => BuildComplianceItemPayload(item, categories, users)).ToList());
    }

    public async Task<ServiceResult<object>> CreateItemAsync(CreateComplianceItemRequest request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default)
    {
        ComplianceValidators.ValidateCreateItem(request);

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(request.ClientId))
        {
            return ServiceResult<object>.ForbiddenResult();
        }

        var status = NormalizeStatus(request.Status);
        var riskLevel = NormalizeRiskLevel(request.RiskLevel);

        if (!AllowedItemStatuses.Contains(status))
        {
            return ServiceResult<object>.ErrorResult("Invalid compliance status.");
        }

        if (!AllowedRiskLevels.Contains(riskLevel))
        {
            return ServiceResult<object>.ErrorResult("Risk level must be low, medium, high, or critical.");
        }

        var categoryExists = await _db.ComplianceCategories.AnyAsync(x => x.Id == request.CategoryId && x.IsActive, ct);
        if (!categoryExists)
        {
            return ServiceResult<object>.ErrorResult("Compliance category not found or inactive.");
        }

        if (!string.IsNullOrWhiteSpace(request.OwnerUserId))
        {
            var ownerExists = await _db.Users.AnyAsync(x => x.Id == request.OwnerUserId, ct);
            if (!ownerExists)
            {
                return ServiceResult<object>.ErrorResult("Owner user was not found.");
            }
        }

        var item = new ComplianceItem
        {
            Id = $"ci_{Guid.NewGuid():N}",
            ClientId = request.ClientId,
            CategoryId = request.CategoryId,
            Name = request.Name.Trim(),
            Status = status,
            OwnerUserId = string.IsNullOrWhiteSpace(request.OwnerUserId) ? null : request.OwnerUserId.Trim(),
            RiskLevel = riskLevel,
            RequiredDocumentCategory = string.IsNullOrWhiteSpace(request.RequiredDocumentCategory) ? null : NormalizeDocumentCategory(request.RequiredDocumentCategory),
            DueDateUtc = request.DueDateUtc,
            ExpiryDateUtc = request.ExpiryDateUtc,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _db.ComplianceItems.Add(item);
        await _db.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(user, "compliance.item_created", "compliance_item", item.Id, item.ClientId, JsonSerializer.Serialize(new { item.CategoryId, item.Status, item.OwnerUserId, item.RiskLevel }), ct);

        var categories = await _db.ComplianceCategories.ToDictionaryAsync(x => x.Id, ct);
        var users = await _db.Users.ToDictionaryAsync(x => x.Id, ct);
        return ServiceResult<object>.Success(BuildComplianceItemPayload(item, categories, users));
    }

    public async Task<ServiceResult<object>> UpdateItemAsync(string id, UpdateComplianceItemRequest request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default)
    {
        ComplianceValidators.ValidateUpdateItem(request);

        var item = await _db.ComplianceItems.FindAsync([id], ct);
        if (item is null)
        {
            return ServiceResult<object>.NotFoundResult();
        }

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(item.ClientId))
        {
            return ServiceResult<object>.ForbiddenResult();
        }

        var status = NormalizeStatus(request.Status);
        var riskLevel = NormalizeRiskLevel(request.RiskLevel);
        if (!AllowedItemStatuses.Contains(status))
        {
            return ServiceResult<object>.ErrorResult("Invalid compliance status.");
        }

        if (!AllowedRiskLevels.Contains(riskLevel))
        {
            return ServiceResult<object>.ErrorResult("Risk level must be low, medium, high, or critical.");
        }

        if (!string.IsNullOrWhiteSpace(request.OwnerUserId))
        {
            var ownerExists = await _db.Users.AnyAsync(x => x.Id == request.OwnerUserId, ct);
            if (!ownerExists)
            {
                return ServiceResult<object>.ErrorResult("Owner user was not found.");
            }
        }

        item.Name = request.Name.Trim();
        item.Status = status;
        item.OwnerUserId = string.IsNullOrWhiteSpace(request.OwnerUserId) ? null : request.OwnerUserId.Trim();
        item.RiskLevel = riskLevel;
        item.RequiredDocumentCategory = string.IsNullOrWhiteSpace(request.RequiredDocumentCategory) ? null : NormalizeDocumentCategory(request.RequiredDocumentCategory);
        item.LinkedDocumentId = string.IsNullOrWhiteSpace(request.LinkedDocumentId) ? null : request.LinkedDocumentId.Trim();
        item.DueDateUtc = request.DueDateUtc;
        item.ExpiryDateUtc = request.ExpiryDateUtc;
        item.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(user, "compliance.item_updated", "compliance_item", item.Id, item.ClientId, JsonSerializer.Serialize(new { item.Status, item.LinkedDocumentId, item.OwnerUserId, item.RiskLevel }), ct);

        var categories = await _db.ComplianceCategories.ToDictionaryAsync(x => x.Id, ct);
        var users = await _db.Users.ToDictionaryAsync(x => x.Id, ct);
        return ServiceResult<object>.Success(BuildComplianceItemPayload(item, categories, users));
    }

    public async Task<ServiceResult<IReadOnlyList<object>>> GetAlertsAsync(System.Security.Claims.ClaimsPrincipal user, string? clientId = null, CancellationToken ct = default)
    {
        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        var scopedClientIds = allowedClientIds;

        if (!string.IsNullOrWhiteSpace(clientId))
        {
            if (!allowedClientIds.Contains(clientId))
            {
                return ServiceResult<IReadOnlyList<object>>.ForbiddenResult();
            }

            scopedClientIds = [clientId];
        }

        var categories = await _db.ComplianceCategories.ToDictionaryAsync(x => x.Id, ct);
        var users = await _db.Users.ToDictionaryAsync(x => x.Id, ct);
        var items = await _db.ComplianceItems
            .Where(x => scopedClientIds.Contains(x.ClientId))
            .OrderBy(x => x.ClientId)
            .ThenBy(x => x.ExpiryDateUtc)
            .ToListAsync(ct);

        return ServiceResult<IReadOnlyList<object>>.Success(items
            .Select(item => BuildAlert(item, categories, users))
            .Where(alert => alert is not null)
            .Cast<object>()
            .ToList());
    }

    public async Task<ServiceResult<IReadOnlyList<ComplianceReminder>>> GetRemindersAsync(System.Security.Claims.ClaimsPrincipal user, string? clientId = null, CancellationToken ct = default)
    {
        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        var query = _db.ComplianceReminders.Where(x => allowedClientIds.Contains(x.ClientId));

        if (!string.IsNullOrWhiteSpace(clientId))
        {
            if (!allowedClientIds.Contains(clientId))
            {
                return ServiceResult<IReadOnlyList<ComplianceReminder>>.ForbiddenResult();
            }

            query = query.Where(x => x.ClientId == clientId);
        }

        var results = await query.OrderByDescending(x => x.ScheduledForUtc).ToListAsync(ct);
        return ServiceResult<IReadOnlyList<ComplianceReminder>>.Success(results);
    }

    public async Task<ServiceResult<ComplianceReminder>> CreateReminderAsync(CreateComplianceReminderRequest request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default)
    {
        ComplianceValidators.ValidateCreateReminder(request);

        var complianceItem = await _db.ComplianceItems.FindAsync([request.ComplianceItemId], ct);
        if (complianceItem is null)
        {
            return ServiceResult<ComplianceReminder>.ErrorResult("Compliance item not found.");
        }

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(complianceItem.ClientId))
        {
            return ServiceResult<ComplianceReminder>.ForbiddenResult();
        }

        var recipientExists = await _db.Users.AnyAsync(x => x.Id == request.RecipientUserId, ct);
        if (!recipientExists)
        {
            return ServiceResult<ComplianceReminder>.ErrorResult("Reminder recipient user was not found.");
        }

        var reminder = new ComplianceReminder
        {
            Id = $"cr_{Guid.NewGuid():N}",
            ComplianceItemId = request.ComplianceItemId,
            ClientId = complianceItem.ClientId,
            RecipientUserId = request.RecipientUserId,
            Type = request.Type.Trim().ToLowerInvariant(),
            Status = "pending",
            ScheduledForUtc = request.ScheduledForUtc,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.ComplianceReminders.Add(reminder);
        await _db.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(user, "compliance.reminder_created", "compliance_reminder", reminder.Id, reminder.ClientId, JsonSerializer.Serialize(new { reminder.Type, reminder.ScheduledForUtc }), ct);

        await _db.AddNotificationsAsync(
            user,
            [reminder.RecipientUserId],
            reminder.ClientId,
            "compliance.reminder",
            "Compliance reminder scheduled",
            $"Compliance reminder for {complianceItem.Name} is scheduled.",
            "/client/compliance",
            new { reminder.Id, reminder.Type, reminder.ScheduledForUtc },
            ct);

        return ServiceResult<ComplianceReminder>.Success(reminder);
    }

    public async Task<ServiceResult<ComplianceReminder>> UpdateReminderStatusAsync(string id, UpdateComplianceReminderStatusRequest request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default)
    {
        ComplianceValidators.ValidateReminderStatus(request);

        var item = await _db.ComplianceReminders.FindAsync([id], ct);
        if (item is null)
        {
            return ServiceResult<ComplianceReminder>.NotFoundResult();
        }

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(item.ClientId))
        {
            return ServiceResult<ComplianceReminder>.ForbiddenResult();
        }

        var normalized = request.Status.Trim().ToLowerInvariant();
        if (!AllowedReminderStatuses.Contains(normalized))
        {
            return ServiceResult<ComplianceReminder>.ErrorResult("Invalid reminder status.");
        }

        item.Status = normalized;
        item.SentAtUtc = normalized == "sent" ? DateTime.UtcNow : item.SentAtUtc;

        await _db.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(user, "compliance.reminder_status_updated", "compliance_reminder", item.Id, item.ClientId, JsonSerializer.Serialize(new { item.Status }), ct);
        return ServiceResult<ComplianceReminder>.Success(item);
    }

    public async Task<ServiceResult<object>> GetSummaryReportAsync(System.Security.Claims.ClaimsPrincipal user, string? clientId = null, CancellationToken ct = default)
    {
        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        var scopedClientIds = allowedClientIds;

        if (!string.IsNullOrWhiteSpace(clientId))
        {
            if (!allowedClientIds.Contains(clientId))
            {
                return ServiceResult<object>.ForbiddenResult();
            }

            scopedClientIds = [clientId];
        }

        var items = await _db.ComplianceItems.Where(x => scopedClientIds.Contains(x.ClientId)).ToListAsync(ct);
        var categories = await _db.ComplianceCategories.ToDictionaryAsync(x => x.Id, ct);

        var report = items
            .GroupBy(x => x.ClientId)
            .Select(group => new
            {
                clientId = group.Key,
                total = group.Count(),
                valid = group.Count(x => x.Status == "valid"),
                expiringSoon = group.Count(x => x.Status == "expiring_soon"),
                expired = group.Count(x => x.Status == "expired"),
                missing = group.Count(x => x.Status == "missing"),
                pending = group.Count(x => x.Status == "pending"),
                rejected = group.Count(x => x.Status == "rejected"),
                criticalRisk = group.Count(x => x.RiskLevel == "critical"),
                highRisk = group.Count(x => x.RiskLevel == "high"),
                complianceScore = group.Count() == 0
                    ? 0
                    : (int)Math.Round((double)group.Count(x => x.Status == "valid") / group.Count() * 100),
                categories = group
                    .GroupBy(x => x.CategoryId)
                    .Select(categoryGroup => new
                    {
                        categoryId = categoryGroup.Key,
                        categoryName = categories.GetValueOrDefault(categoryGroup.Key)?.Name,
                        total = categoryGroup.Count(),
                        valid = categoryGroup.Count(x => x.Status == "valid"),
                        expired = categoryGroup.Count(x => x.Status == "expired"),
                        highRisk = categoryGroup.Count(x => x.RiskLevel is "high" or "critical")
                    })
                    .OrderBy(x => x.categoryName)
                    .ToList()
            })
            .OrderBy(x => x.clientId)
            .ToList();

        return ServiceResult<object>.Success(new
        {
            generatedAtUtc = DateTime.UtcNow,
            clients = report,
            totals = new
            {
                totalItems = items.Count,
                valid = items.Count(x => x.Status == "valid"),
                expiringSoon = items.Count(x => x.Status == "expiring_soon"),
                expired = items.Count(x => x.Status == "expired"),
                missing = items.Count(x => x.Status == "missing"),
                criticalRisk = items.Count(x => x.RiskLevel == "critical"),
                highRisk = items.Count(x => x.RiskLevel == "high")
            }
        });
    }

    private static string NormalizeStatus(string raw) => raw.Trim().ToLowerInvariant();
    private static string NormalizeRiskLevel(string raw) => raw.Trim().ToLowerInvariant();
    private static string NormalizeDocumentCategory(string raw) => raw.Trim().ToLowerInvariant().Replace("-", "_").Replace(" ", "_");

    private static string GenerateCategoryCode(string name)
    {
        var compact = new string(name.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        return compact.Length switch
        {
            0 => "GEN",
            <= 8 => compact,
            _ => compact[..8]
        };
    }

    private static object BuildComplianceItemPayload(ComplianceItem item, IReadOnlyDictionary<string, ComplianceCategory> categories, IReadOnlyDictionary<string, User> users)
    {
        var category = categories.GetValueOrDefault(item.CategoryId);
        var owner = string.IsNullOrWhiteSpace(item.OwnerUserId) ? null : users.GetValueOrDefault(item.OwnerUserId);

        return new
        {
            item.Id,
            item.ClientId,
            item.CategoryId,
            categoryName = category?.Name,
            categoryCode = category?.Code,
            item.Name,
            item.Status,
            item.OwnerUserId,
            ownerName = owner?.FullName,
            item.RequiredDocumentCategory,
            item.LinkedDocumentId,
            item.RiskLevel,
            item.DueDateUtc,
            item.ExpiryDateUtc,
            alertLevel = ComputeAlertLevel(item),
            item.CreatedAtUtc,
            item.UpdatedAtUtc
        };
    }

    private static object? BuildAlert(ComplianceItem item, IReadOnlyDictionary<string, ComplianceCategory> categories, IReadOnlyDictionary<string, User> users)
    {
        var alertLevel = ComputeAlertLevel(item);
        if (alertLevel is null)
        {
            return null;
        }

        var category = categories.GetValueOrDefault(item.CategoryId);
        var owner = string.IsNullOrWhiteSpace(item.OwnerUserId) ? null : users.GetValueOrDefault(item.OwnerUserId);

        return new
        {
            complianceItemId = item.Id,
            item.ClientId,
            item.Name,
            categoryName = category?.Name,
            item.Status,
            item.RiskLevel,
            item.ExpiryDateUtc,
            item.DueDateUtc,
            ownerUserId = item.OwnerUserId,
            ownerName = owner?.FullName,
            alertLevel,
            message = BuildAlertMessage(item, alertLevel)
        };
    }

    private static string? ComputeAlertLevel(ComplianceItem item)
    {
        var now = DateTime.UtcNow;
        if (item.Status == "expired")
        {
            return "critical";
        }

        if (item.Status == "rejected")
        {
            return "high";
        }

        if (item.ExpiryDateUtc is DateTime expiry)
        {
            var daysUntilExpiry = (expiry.Date - now.Date).TotalDays;
            if (daysUntilExpiry < 0)
            {
                return "critical";
            }

            if (daysUntilExpiry <= 7)
            {
                return item.RiskLevel == "critical" ? "critical" : "high";
            }

            if (daysUntilExpiry <= 30)
            {
                return "medium";
            }
        }

        if (item.Status is "missing" or "pending")
        {
            return item.RiskLevel is "critical" or "high" ? "high" : "medium";
        }

        return null;
    }

    private static string BuildAlertMessage(ComplianceItem item, string alertLevel)
    {
        if (item.Status == "expired")
        {
            return $"{item.Name} is expired and requires immediate attention.";
        }

        if (item.Status == "rejected")
        {
            return $"{item.Name} was rejected and needs remediation.";
        }

        if (item.ExpiryDateUtc is DateTime expiry)
        {
            return $"{item.Name} is {alertLevel} risk and expires on {expiry:yyyy-MM-dd}.";
        }

        return $"{item.Name} is {alertLevel} risk and needs follow-up.";
    }
}
