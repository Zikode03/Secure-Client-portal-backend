using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Models;
using System.Text.Json;

namespace SecureClientPortal.Backend.Controllers;

public record CreateComplianceCategoryRequest(string Name, string Description, string? Code, bool IsActive = true);
public record CreateComplianceItemRequest(
    string ClientId,
    string CategoryId,
    string Name,
    string Status,
    string? OwnerUserId,
    string RiskLevel,
    string? RequiredDocumentCategory,
    DateTime? DueDateUtc,
    DateTime? ExpiryDateUtc);
public record UpdateComplianceItemRequest(
    string Name,
    string Status,
    string? OwnerUserId,
    string RiskLevel,
    string? RequiredDocumentCategory,
    string? LinkedDocumentId,
    DateTime? DueDateUtc,
    DateTime? ExpiryDateUtc);
public record CreateComplianceReminderRequest(string ComplianceItemId, string RecipientUserId, string Type, DateTime ScheduledForUtc);

[ApiController]
[Route("api/compliance")]
[Authorize(Policy = "ClientOrAccountant")]
public class ComplianceController : ControllerBase
{
    private static readonly HashSet<string> AllowedItemStatuses =
    ["missing", "pending", "valid", "expiring_soon", "expired", "rejected"];

    private static readonly HashSet<string> AllowedReminderStatuses =
    ["pending", "sent", "dismissed"];

    private static readonly HashSet<string> AllowedRiskLevels =
    ["low", "medium", "high", "critical"];

    private readonly PortalDbContext _db;

    public ComplianceController(PortalDbContext db)
    {
        _db = db;
    }

    [HttpGet("categories")]
    public async Task<ActionResult<IEnumerable<ComplianceCategory>>> GetCategories()
    {
        return Ok(await _db.ComplianceCategories.OrderBy(x => x.Name).ToListAsync());
    }

    [HttpPost("categories")]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<ActionResult<ComplianceCategory>> CreateCategory([FromBody] CreateComplianceCategoryRequest request)
    {
        var normalizedCode = string.IsNullOrWhiteSpace(request.Code)
            ? GenerateCategoryCode(request.Name)
            : request.Code.Trim().ToUpperInvariant();

        if (await _db.ComplianceCategories.AnyAsync(x => x.Code == normalizedCode))
        {
            return Conflict(new { error = "Compliance category code already exists." });
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
        await _db.SaveChangesAsync();
        await _db.WriteAuditLogAsync(User, "compliance.category_created", "compliance_category", item.Id, null, JsonSerializer.Serialize(new { item.Name, item.Code }));
        return Ok(item);
    }

    [HttpPost("categories/seed-defaults")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> SeedDefaultCategories()
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
            var existing = await _db.ComplianceCategories.FirstOrDefaultAsync(x => x.Id == category.Id || x.Code == category.Code);
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

        await _db.SaveChangesAsync();
        await _db.WriteAuditLogAsync(User, "compliance.categories_seeded", "compliance_category", "defaults", null);
        return Ok(new { seeded = defaults.Length });
    }

    [HttpGet("items")]
    public async Task<ActionResult<IEnumerable<object>>> GetItems([FromQuery] string? clientId = null)
    {
        var allowedClientIds = await User.GetAccessibleClientIdsAsync(_db);
        var query = _db.ComplianceItems.Where(x => allowedClientIds.Contains(x.ClientId));

        if (!string.IsNullOrWhiteSpace(clientId))
        {
            if (!allowedClientIds.Contains(clientId))
            {
                return Forbid();
            }
            query = query.Where(x => x.ClientId == clientId);
        }

        var categories = await _db.ComplianceCategories.ToDictionaryAsync(x => x.Id);
        var users = await _db.Users.ToDictionaryAsync(x => x.Id);
        var items = await query.OrderBy(x => x.ClientId).ThenBy(x => x.Name).ToListAsync();

        return Ok(items.Select(item => BuildComplianceItemPayload(item, categories, users)));
    }

    [HttpPost("items")]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<ActionResult<object>> CreateItem([FromBody] CreateComplianceItemRequest request)
    {
        var allowedClientIds = await User.GetAccessibleClientIdsAsync(_db);
        if (!allowedClientIds.Contains(request.ClientId))
        {
            return Forbid();
        }

        var status = NormalizeStatus(request.Status);
        if (!AllowedItemStatuses.Contains(status))
        {
            return BadRequest(new { error = "Invalid compliance status." });
        }

        var riskLevel = NormalizeRiskLevel(request.RiskLevel);
        if (!AllowedRiskLevels.Contains(riskLevel))
        {
            return BadRequest(new { error = "Risk level must be low, medium, high, or critical." });
        }

        var categoryExists = await _db.ComplianceCategories.AnyAsync(x => x.Id == request.CategoryId && x.IsActive);
        if (!categoryExists)
        {
            return BadRequest(new { error = "Compliance category not found or inactive." });
        }

        if (!string.IsNullOrWhiteSpace(request.OwnerUserId))
        {
            var ownerExists = await _db.Users.AnyAsync(x => x.Id == request.OwnerUserId);
            if (!ownerExists)
            {
                return BadRequest(new { error = "Owner user was not found." });
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
        await _db.SaveChangesAsync();
        await _db.WriteAuditLogAsync(User, "compliance.item_created", "compliance_item", item.Id, item.ClientId, JsonSerializer.Serialize(new { item.CategoryId, item.Status, item.OwnerUserId, item.RiskLevel }));

        var categories = await _db.ComplianceCategories.ToDictionaryAsync(x => x.Id);
        var users = await _db.Users.ToDictionaryAsync(x => x.Id);
        return Ok(BuildComplianceItemPayload(item, categories, users));
    }

    [HttpPut("items/{id}")]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<ActionResult<object>> UpdateItem(string id, [FromBody] UpdateComplianceItemRequest request)
    {
        var item = await _db.ComplianceItems.FindAsync(id);
        if (item is null)
        {
            return NotFound();
        }

        var allowedClientIds = await User.GetAccessibleClientIdsAsync(_db);
        if (!allowedClientIds.Contains(item.ClientId))
        {
            return Forbid();
        }

        var status = NormalizeStatus(request.Status);
        if (!AllowedItemStatuses.Contains(status))
        {
            return BadRequest(new { error = "Invalid compliance status." });
        }

        var riskLevel = NormalizeRiskLevel(request.RiskLevel);
        if (!AllowedRiskLevels.Contains(riskLevel))
        {
            return BadRequest(new { error = "Risk level must be low, medium, high, or critical." });
        }

        if (!string.IsNullOrWhiteSpace(request.OwnerUserId))
        {
            var ownerExists = await _db.Users.AnyAsync(x => x.Id == request.OwnerUserId);
            if (!ownerExists)
            {
                return BadRequest(new { error = "Owner user was not found." });
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

        await _db.SaveChangesAsync();
        await _db.WriteAuditLogAsync(User, "compliance.item_updated", "compliance_item", item.Id, item.ClientId, JsonSerializer.Serialize(new { item.Status, item.LinkedDocumentId, item.OwnerUserId, item.RiskLevel }));

        var categories = await _db.ComplianceCategories.ToDictionaryAsync(x => x.Id);
        var users = await _db.Users.ToDictionaryAsync(x => x.Id);
        return Ok(BuildComplianceItemPayload(item, categories, users));
    }

    [HttpGet("alerts")]
    public async Task<ActionResult<IEnumerable<object>>> GetAlerts([FromQuery] string? clientId = null)
    {
        var allowedClientIds = await User.GetAccessibleClientIdsAsync(_db);
        var scopedClientIds = allowedClientIds;

        if (!string.IsNullOrWhiteSpace(clientId))
        {
            if (!allowedClientIds.Contains(clientId))
            {
                return Forbid();
            }

            scopedClientIds = [clientId];
        }

        var categories = await _db.ComplianceCategories.ToDictionaryAsync(x => x.Id);
        var users = await _db.Users.ToDictionaryAsync(x => x.Id);
        var items = await _db.ComplianceItems
            .Where(x => scopedClientIds.Contains(x.ClientId))
            .OrderBy(x => x.ClientId)
            .ThenBy(x => x.ExpiryDateUtc)
            .ToListAsync();

        return Ok(items
            .Select(item => BuildAlert(item, categories, users))
            .Where(alert => alert is not null)
            .Cast<object>()
            .ToList());
    }

    [HttpGet("reminders")]
    public async Task<ActionResult<IEnumerable<ComplianceReminder>>> GetReminders([FromQuery] string? clientId = null)
    {
        var allowedClientIds = await User.GetAccessibleClientIdsAsync(_db);
        var query = _db.ComplianceReminders.Where(x => allowedClientIds.Contains(x.ClientId));

        if (!string.IsNullOrWhiteSpace(clientId))
        {
            if (!allowedClientIds.Contains(clientId))
            {
                return Forbid();
            }
            query = query.Where(x => x.ClientId == clientId);
        }

        return Ok(await query.OrderByDescending(x => x.ScheduledForUtc).ToListAsync());
    }

    [HttpPost("reminders")]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<ActionResult<ComplianceReminder>> CreateReminder([FromBody] CreateComplianceReminderRequest request)
    {
        var complianceItem = await _db.ComplianceItems.FindAsync(request.ComplianceItemId);
        if (complianceItem is null)
        {
            return BadRequest(new { error = "Compliance item not found." });
        }

        var allowedClientIds = await User.GetAccessibleClientIdsAsync(_db);
        if (!allowedClientIds.Contains(complianceItem.ClientId))
        {
            return Forbid();
        }

        var recipientExists = await _db.Users.AnyAsync(x => x.Id == request.RecipientUserId);
        if (!recipientExists)
        {
            return BadRequest(new { error = "Reminder recipient user was not found." });
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
        await _db.SaveChangesAsync();
        await _db.WriteAuditLogAsync(User, "compliance.reminder_created", "compliance_reminder", reminder.Id, reminder.ClientId, JsonSerializer.Serialize(new { reminder.Type, reminder.ScheduledForUtc }));

        await _db.AddNotificationsAsync(
            User,
            [reminder.RecipientUserId],
            reminder.ClientId,
            "compliance.reminder",
            "Compliance reminder scheduled",
            $"Compliance reminder for {complianceItem.Name} is scheduled.",
            "/client/compliance",
            new { reminder.Id, reminder.Type, reminder.ScheduledForUtc });

        return Ok(reminder);
    }

    [HttpPut("reminders/{id}/status")]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<ActionResult<ComplianceReminder>> UpdateReminderStatus(string id, [FromBody] string status)
    {
        var item = await _db.ComplianceReminders.FindAsync(id);
        if (item is null)
        {
            return NotFound();
        }

        var allowedClientIds = await User.GetAccessibleClientIdsAsync(_db);
        if (!allowedClientIds.Contains(item.ClientId))
        {
            return Forbid();
        }

        var normalized = status.Trim().ToLowerInvariant();
        if (!AllowedReminderStatuses.Contains(normalized))
        {
            return BadRequest(new { error = "Invalid reminder status." });
        }

        item.Status = normalized;
        item.SentAtUtc = normalized == "sent" ? DateTime.UtcNow : item.SentAtUtc;

        await _db.SaveChangesAsync();
        await _db.WriteAuditLogAsync(User, "compliance.reminder_status_updated", "compliance_reminder", item.Id, item.ClientId, JsonSerializer.Serialize(new { item.Status }));
        return Ok(item);
    }

    [HttpGet("reports/summary")]
    public async Task<ActionResult> GetSummaryReport([FromQuery] string? clientId = null)
    {
        var allowedClientIds = await User.GetAccessibleClientIdsAsync(_db);
        var scopedClientIds = allowedClientIds;

        if (!string.IsNullOrWhiteSpace(clientId))
        {
            if (!allowedClientIds.Contains(clientId))
            {
                return Forbid();
            }
            scopedClientIds = [clientId];
        }

        var items = await _db.ComplianceItems.Where(x => scopedClientIds.Contains(x.ClientId)).ToListAsync();
        var categories = await _db.ComplianceCategories.ToDictionaryAsync(x => x.Id);

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

        return Ok(new
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

    private static string NormalizeDocumentCategory(string raw)
    {
        return raw.Trim().ToLowerInvariant().Replace("-", "_").Replace(" ", "_");
    }

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

    private static object BuildComplianceItemPayload(
        ComplianceItem item,
        IReadOnlyDictionary<string, ComplianceCategory> categories,
        IReadOnlyDictionary<string, User> users)
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

    private static object? BuildAlert(
        ComplianceItem item,
        IReadOnlyDictionary<string, ComplianceCategory> categories,
        IReadOnlyDictionary<string, User> users)
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
