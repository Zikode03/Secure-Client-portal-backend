using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts.Modules.Compliance;
using SecureClientPortal.Backend.Application.Modules.Compliance;
using SecureClientPortal.Backend.Application.Modules.Documents;
using SecureClientPortal.Backend.Application.Modules.Requests;
using SecureClientPortal.Backend.Application.Contracts.Modules.Requests;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SecureClientPortal.Backend.Infrastructure.Modules.Compliance.Application;

public sealed class ComplianceService : IComplianceService
{
    private static readonly HashSet<string> AllowedEvidenceExtensions = [".pdf", ".png", ".jpg", ".jpeg", ".doc", ".docx", ".xls", ".xlsx"];
    private static readonly HashSet<string> AllowedItemStatuses = ["missing", "pending", "valid", "expiring_soon", "expired", "rejected"];
    private static readonly HashSet<string> AllowedReminderStatuses = ["pending", "sent", "dismissed"];
    private static readonly HashSet<string> AllowedRiskLevels = ["low", "medium", "high", "critical"];

    private readonly PortalDbContext _db;
    private readonly ComplianceAssessmentDomainService _complianceAssessmentDomainService;
    private readonly IFileStorage? _fileStorage;
    private readonly IRequestService? _requestService;

    public ComplianceService(PortalDbContext db, IFileStorage? fileStorage = null, IRequestService? requestService = null)
    {
        _db = db;
        _complianceAssessmentDomainService = new ComplianceAssessmentDomainService();
        _fileStorage = fileStorage;
        _requestService = requestService;
    }

    public async Task<IReadOnlyList<ComplianceCategory>> GetCategoriesAsync(CancellationToken ct = default)
    {
        return await _db.ComplianceCategories.OrderBy(x => x.Name).ToListAsync(ct);
    }

    public async Task<ServiceResult<ComplianceCategory>> CreateCategoryAsync(CreateComplianceCategoryRequest request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default)
    {
        ComplianceValidators.ValidateCategory(request);

        var normalizedCode = string.IsNullOrWhiteSpace(request.Code)
            ? ComplianceAlertPolicy.GenerateCategoryCode(request.Name)
            : request.Code.Trim().ToUpperInvariant();

        if (await _db.ComplianceCategories.AnyAsync(x => x.Code == normalizedCode, ct))
        {
            return ServiceResult<ComplianceCategory>.ErrorResult("Compliance category code already exists.", statusCode: StatusCodes.Status409Conflict);
        }

        var item = ComplianceCategory.Create(
            Guid.NewGuid(),
            request.Name,
            request.Description,
            normalizedCode,
            request.IsActive);

        _db.ComplianceCategories.Add(item);
        await _db.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(user, "compliance.category_created", "compliance_category", item.Id, null, JsonSerializer.Serialize(new { item.Name, item.Code }), ct);
        return ServiceResult<ComplianceCategory>.Success(item);
    }

    public async Task<object> SeedDefaultCategoriesAsync(System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default)
    {
        var defaults = new[]
        {
            ComplianceCategory.Create(DeterministicGuid("cc_tax_compliance"), "Tax Compliance", "Income tax, VAT, and tax authority filing obligations.", "TAX", true),
            ComplianceCategory.Create(DeterministicGuid("cc_cipc_compliance"), "CIPC Compliance", "Company registration, annual returns, and beneficial ownership obligations.", "CIPC", true),
            ComplianceCategory.Create(DeterministicGuid("cc_payroll_compliance"), "Payroll Compliance", "Payroll submissions, UIF, PAYE, and employee records.", "PAYROLL", true),
            ComplianceCategory.Create(DeterministicGuid("cc_popia_compliance"), "POPIA Compliance", "Privacy controls, information processing, and consent evidence.", "POPIA", true)
        };

        foreach (var category in defaults)
        {
            var existing = await _db.ComplianceCategories.FirstOrDefaultAsync(x => x.Id == category.Id || x.Code == category.Code, ct);
            if (existing is null)
            {
                _db.ComplianceCategories.Add(category);
            }
            else
            {
                existing.UpdateDetails(category.Name, category.Description, category.Code, true);
            }
        }

        await _db.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(user, "compliance.categories_seeded", "compliance_category", DeterministicGuid("compliance.categories_seeded"), null, null, ct);
        return new { seeded = defaults.Length };
    }

    public async Task<ServiceResult<IReadOnlyList<object>>> GetItemsAsync(System.Security.Claims.ClaimsPrincipal user, string? clientId = null, CancellationToken ct = default)
    {
        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        var query = _db.ComplianceItems.Where(x => allowedClientIds.Contains(x.ClientId));

        if (Guid.TryParse(clientId, out var parsedClientId))
        {
            if (!allowedClientIds.Contains(parsedClientId))
            {
                return ServiceResult<IReadOnlyList<object>>.ForbiddenResult();
            }

            query = query.Where(x => x.ClientId == parsedClientId);
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

        if (request.OwnerUserId.HasValue)
        {
            var ownerExists = await _db.Users.AnyAsync(x => x.Id == request.OwnerUserId, ct);
            if (!ownerExists)
            {
                return ServiceResult<object>.ErrorResult("Owner user was not found.");
            }
        }

        var item = ComplianceItem.Create(
            Guid.NewGuid(),
            request.ClientId,
            request.CategoryId,
            request.Name,
            ComplianceDomainValues.ToComplianceItemStatus(status),
            request.OwnerUserId,
            ComplianceDomainValues.ToComplianceRiskLevel(riskLevel),
            request.RequiredDocumentCategory,
            request.DueDateUtc,
            request.ExpiryDateUtc);

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

        if (!Guid.TryParse(id, out var complianceItemId))
        {
            return ServiceResult<object>.NotFoundResult();
        }

        var item = await _db.ComplianceItems.FindAsync([complianceItemId], ct);
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

        if (request.OwnerUserId.HasValue)
        {
            var ownerExists = await _db.Users.AnyAsync(x => x.Id == request.OwnerUserId, ct);
            if (!ownerExists)
            {
                return ServiceResult<object>.ErrorResult("Owner user was not found.");
            }
        }

        item.Update(
            request.Name,
            ComplianceDomainValues.ToComplianceItemStatus(status),
            request.OwnerUserId,
            ComplianceDomainValues.ToComplianceRiskLevel(riskLevel),
            request.RequiredDocumentCategory,
            request.LinkedDocumentId,
            request.DueDateUtc,
            request.ExpiryDateUtc);

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

        if (Guid.TryParse(clientId, out var parsedClientId))
        {
            if (!allowedClientIds.Contains(parsedClientId))
            {
                return ServiceResult<IReadOnlyList<object>>.ForbiddenResult();
            }

            scopedClientIds = new HashSet<Guid> { parsedClientId };
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

        if (Guid.TryParse(clientId, out var parsedClientId))
        {
            if (!allowedClientIds.Contains(parsedClientId))
            {
                return ServiceResult<IReadOnlyList<ComplianceReminder>>.ForbiddenResult();
            }

            query = query.Where(x => x.ClientId == parsedClientId);
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

        var reminder = ComplianceReminder.Create(
            Guid.NewGuid(),
            request.ComplianceItemId,
            complianceItem.ClientId,
            request.RecipientUserId,
            request.Type,
            request.ScheduledForUtc);

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

        if (!Guid.TryParse(id, out var reminderId))
        {
            return ServiceResult<ComplianceReminder>.NotFoundResult();
        }

        var item = await _db.ComplianceReminders.FindAsync([reminderId], ct);
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

        item.SetStatus(ComplianceDomainValues.ToComplianceReminderStatus(normalized));

        await _db.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(user, "compliance.reminder_status_updated", "compliance_reminder", item.Id, item.ClientId, JsonSerializer.Serialize(new { item.Status }), ct);
        return ServiceResult<ComplianceReminder>.Success(item);
    }

    public async Task<ServiceResult<object>> GetSummaryReportAsync(System.Security.Claims.ClaimsPrincipal user, string? clientId = null, CancellationToken ct = default)
    {
        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        var scopedClientIds = allowedClientIds;

        if (Guid.TryParse(clientId, out var parsedClientId))
        {
            if (!allowedClientIds.Contains(parsedClientId))
            {
                return ServiceResult<object>.ForbiddenResult();
            }

            scopedClientIds = new HashSet<Guid> { parsedClientId };
        }

        var items = await _db.ComplianceItems.Where(x => scopedClientIds.Contains(x.ClientId)).ToListAsync(ct);
        var categories = await _db.ComplianceCategories.ToDictionaryAsync(x => x.Id, ct);

        var report = items
            .GroupBy(x => x.ClientId)
            .Select(group =>
            {
                var groupItems = group.ToList();
                var assessment = _complianceAssessmentDomainService.Assess(groupItems);

                return new
                {
                    clientId = group.Key,
                    total = assessment.Total,
                    valid = assessment.Valid,
                    expiringSoon = assessment.ExpiringSoon,
                    expired = assessment.Expired,
                    missing = assessment.Missing,
                    pending = assessment.Pending,
                    rejected = assessment.Rejected,
                    criticalRisk = assessment.CriticalRisk,
                    highRisk = assessment.HighRisk,
                    complianceScore = assessment.ComplianceScore,
                    categories = groupItems
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
                };
            })
            .OrderBy(x => x.clientId)
            .ToList();

        var overallAssessment = _complianceAssessmentDomainService.Assess(items);

        return ServiceResult<object>.Success(new
        {
            generatedAtUtc = DateTime.UtcNow,
            clients = report,
            totals = new
            {
                totalItems = overallAssessment.Total,
                valid = overallAssessment.Valid,
                expiringSoon = overallAssessment.ExpiringSoon,
                expired = overallAssessment.Expired,
                missing = overallAssessment.Missing,
                criticalRisk = overallAssessment.CriticalRisk,
                highRisk = overallAssessment.HighRisk
            }
        });
    }

    public async Task<ServiceResult<IReadOnlyList<ComplianceHistoryEntryResponse>>> GetHistoryAsync(
        System.Security.Claims.ClaimsPrincipal user,
        string? clientId = null,
        string? itemId = null,
        int limit = 200,
        CancellationToken ct = default)
    {
        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        var scopedClientIds = allowedClientIds;
        if (!string.IsNullOrWhiteSpace(clientId))
        {
            if (!Guid.TryParse(clientId, out var parsedClientId))
            {
                return ServiceResult<IReadOnlyList<ComplianceHistoryEntryResponse>>.ErrorResult("Client id is invalid.");
            }

            if (!allowedClientIds.Contains(parsedClientId))
            {
                return ServiceResult<IReadOnlyList<ComplianceHistoryEntryResponse>>.ForbiddenResult();
            }

            scopedClientIds = [parsedClientId];
        }

        Guid? parsedItemId = null;
        if (!string.IsNullOrWhiteSpace(itemId))
        {
            if (!Guid.TryParse(itemId, out var value))
            {
                return ServiceResult<IReadOnlyList<ComplianceHistoryEntryResponse>>.ErrorResult("Compliance item id is invalid.");
            }

            var item = await _db.ComplianceItems.FirstOrDefaultAsync(x => x.Id == value, ct);
            if (item is null)
            {
                return ServiceResult<IReadOnlyList<ComplianceHistoryEntryResponse>>.NotFoundResult();
            }

            if (!allowedClientIds.Contains(item.ClientId))
            {
                return ServiceResult<IReadOnlyList<ComplianceHistoryEntryResponse>>.ForbiddenResult();
            }

            parsedItemId = value;
            scopedClientIds = [item.ClientId];
        }

        var query = _db.AuditLogs.Where(x =>
            x.ClientId.HasValue &&
            scopedClientIds.Contains(x.ClientId.Value) &&
            x.Action.StartsWith("compliance."));
        if (parsedItemId.HasValue)
        {
            var itemToken = parsedItemId.Value.ToString();
            query = query.Where(x =>
                x.EntityId == parsedItemId.Value ||
                (x.MetadataJson != null && x.MetadataJson.Contains(itemToken)));
        }

        var logs = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(Math.Clamp(limit, 1, 500))
            .ToListAsync(ct);
        var actorIds = logs.Where(x => x.ActorUserId.HasValue).Select(x => x.ActorUserId!.Value).Distinct().ToArray();
        var actors = await _db.Users.Where(x => actorIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.FullName, ct);

        return ServiceResult<IReadOnlyList<ComplianceHistoryEntryResponse>>.Success(logs.Select(x =>
            new ComplianceHistoryEntryResponse(
                x.Id,
                x.Action,
                x.ActorUserId.HasValue ? actors.GetValueOrDefault(x.ActorUserId.Value) ?? "Unknown user" : "System",
                x.ActorRole,
                x.CreatedAtUtc,
                DescribeHistoryAction(x.Action),
                x.EntityType,
                x.EntityId,
                x.MetadataJson)).ToList());
    }

    public async Task<ServiceResult<IReadOnlyList<ComplianceEvidenceVersionResponse>>> GetEvidenceVersionsAsync(
        string itemId,
        System.Security.Claims.ClaimsPrincipal user,
        CancellationToken ct = default)
    {
        var itemResult = await ResolveAccessibleItemAsync(itemId, user, ct);
        if (itemResult.Error is not null) return ConvertError<IReadOnlyList<ComplianceEvidenceVersionResponse>>(itemResult.Error);

        var versions = await _db.ComplianceEvidenceVersions
            .Where(x => x.ComplianceItemId == itemResult.Item!.Id)
            .OrderByDescending(x => x.VersionNumber)
            .ToListAsync(ct);
        var uploaderIds = versions.Select(x => x.UploadedByUserId).Distinct().ToArray();
        var uploaders = await _db.Users.Where(x => uploaderIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.FullName, ct);
        return ServiceResult<IReadOnlyList<ComplianceEvidenceVersionResponse>>.Success(
            versions.Select(x => MapEvidence(x, uploaders.GetValueOrDefault(x.UploadedByUserId))).ToList());
    }

    public async Task<ServiceResult<ComplianceEvidenceVersionResponse>> UploadEvidenceAsync(
        string itemId,
        UploadComplianceEvidenceRequest request,
        System.Security.Claims.ClaimsPrincipal user,
        CancellationToken ct = default)
    {
        if (request.File is null || request.File.Length <= 0)
        {
            return ServiceResult<ComplianceEvidenceVersionResponse>.ErrorResult("An evidence file is required.");
        }

        if (request.File.Length > DocumentValidators.MaxUploadFileSizeBytes)
        {
            return ServiceResult<ComplianceEvidenceVersionResponse>.ErrorResult("File exceeds the maximum upload size of 100 MB.");
        }

        var extension = Path.GetExtension(request.File.FileName ?? string.Empty).Trim().ToLowerInvariant();
        if (!AllowedEvidenceExtensions.Contains(extension))
        {
            return ServiceResult<ComplianceEvidenceVersionResponse>.ErrorResult("Unsupported file type. Allowed file types: .pdf, .png, .jpg, .jpeg, .doc, .docx, .xls, .xlsx.");
        }

        if (_fileStorage is null)
        {
            return ServiceResult<ComplianceEvidenceVersionResponse>.ErrorResult("Evidence storage is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var itemResult = await ResolveAccessibleItemAsync(itemId, user, ct);
        if (itemResult.Error is not null) return ConvertError<ComplianceEvidenceVersionResponse>(itemResult.Error);
        var item = itemResult.Item!;
        var actorUserId = user.GetUserId();
        if (!actorUserId.HasValue)
        {
            return ServiceResult<ComplianceEvidenceVersionResponse>.UnauthorizedResult();
        }

        var stored = await _fileStorage.SaveAsync(request.File, item.ClientId.ToString(), ct);
        var currentVersions = await _db.ComplianceEvidenceVersions
            .Where(x => x.ComplianceItemId == item.Id && x.IsCurrentVersion)
            .ToListAsync(ct);
        foreach (var current in currentVersions)
        {
            current.MarkNotCurrent();
        }

        var nextVersion = (await _db.ComplianceEvidenceVersions
            .Where(x => x.ComplianceItemId == item.Id)
            .MaxAsync(x => (int?)x.VersionNumber, ct) ?? 0) + 1;
        var version = ComplianceEvidenceVersion.Create(
            Guid.NewGuid(),
            item.Id,
            item.ClientId,
            nextVersion,
            stored.OriginalFileName,
            stored.ContentType,
            stored.SizeBytes,
            stored.StorageKey,
            actorUserId.Value,
            request.Note);
        item.SubmitEvidence();
        _db.ComplianceEvidenceVersions.Add(version);
        await _db.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(
            user,
            "compliance.evidence_uploaded",
            "compliance_evidence_version",
            version.Id,
            item.ClientId,
            JsonSerializer.Serialize(new { complianceItemId = item.Id, version.VersionNumber, version.FileName, version.Note }),
            ct);

        return ServiceResult<ComplianceEvidenceVersionResponse>.Success(MapEvidence(version, user.Identity?.Name));
    }

    public async Task<ServiceResult<(StoredFileContent Content, string FileName)>> DownloadEvidenceAsync(
        string versionId,
        System.Security.Claims.ClaimsPrincipal user,
        CancellationToken ct = default)
    {
        if (!Guid.TryParse(versionId, out var parsedVersionId))
        {
            return ServiceResult<(StoredFileContent, string)>.NotFoundResult();
        }

        if (_fileStorage is null)
        {
            return ServiceResult<(StoredFileContent, string)>.ErrorResult("Evidence storage is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var version = await _db.ComplianceEvidenceVersions.FirstOrDefaultAsync(x => x.Id == parsedVersionId, ct);
        if (version is null)
        {
            return ServiceResult<(StoredFileContent, string)>.NotFoundResult();
        }

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(version.ClientId))
        {
            return ServiceResult<(StoredFileContent, string)>.ForbiddenResult();
        }

        var content = await _fileStorage.OpenReadAsync(version.StorageKey, ct);
        return content is null
            ? ServiceResult<(StoredFileContent, string)>.NotFoundResult("Evidence file content was not found.")
            : ServiceResult<(StoredFileContent, string)>.Success((content, version.FileName));
    }

    public async Task<ServiceResult<RequestItem>> CreateWorkflowRequestAsync(
        string itemId,
        CreateComplianceWorkflowRequest request,
        System.Security.Claims.ClaimsPrincipal user,
        CancellationToken ct = default)
    {
        if (_requestService is null)
        {
            return ServiceResult<RequestItem>.ErrorResult("Request workflow is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var itemResult = await ResolveAccessibleItemAsync(itemId, user, ct);
        if (itemResult.Error is not null) return ConvertError<RequestItem>(itemResult.Error);
        var item = itemResult.Item!;
        var requestType = NormalizeComplianceRequestType(request.RequestType);
        var description = string.IsNullOrWhiteSpace(request.Comments)
            ? $"Please provide the required information or evidence for {item.Name}."
            : request.Comments.Trim();
        var created = await _requestService.CreateAsync(
            new CreateRequestRequest(
                item.ClientId,
                requestType,
                $"Compliance: {item.Name}",
                description,
                requestType == "clarification_needed" ? "medium" : "high",
                request.DueDateUtc,
                item.LinkedDocumentId),
            user,
            ct);
        if (created.forbidden)
        {
            return ServiceResult<RequestItem>.ForbiddenResult();
        }

        await _db.WriteAuditLogAsync(
            user,
            "compliance.request_created",
            "compliance_item",
            item.Id,
            item.ClientId,
            JsonSerializer.Serialize(new { complianceItemId = item.Id, requestId = created.created.Id, requestType }),
            ct);
        return ServiceResult<RequestItem>.Success(created.created);
    }

    private async Task<(ComplianceItem? Item, ServiceResult<object>? Error)> ResolveAccessibleItemAsync(
        string itemId,
        System.Security.Claims.ClaimsPrincipal user,
        CancellationToken ct)
    {
        if (!Guid.TryParse(itemId, out var parsedItemId))
        {
            return (null, ServiceResult<object>.NotFoundResult());
        }

        var item = await _db.ComplianceItems.FirstOrDefaultAsync(x => x.Id == parsedItemId, ct);
        if (item is null)
        {
            return (null, ServiceResult<object>.NotFoundResult());
        }

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        return allowedClientIds.Contains(item.ClientId)
            ? (item, null)
            : (null, ServiceResult<object>.ForbiddenResult());
    }

    private static ComplianceEvidenceVersionResponse MapEvidence(ComplianceEvidenceVersion version, string? uploadedBy) =>
        new(
            version.Id,
            version.ComplianceItemId,
            version.ClientId,
            version.VersionNumber,
            version.FileName,
            version.ContentType,
            version.SizeBytes,
            version.UploadedByUserId,
            uploadedBy,
            version.Note,
            version.IsCurrentVersion,
            version.UploadedAtUtc,
            $"/api/compliance/evidence/{version.Id}/download");

    private static string NormalizeComplianceRequestType(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "missing_document_request" or "missing_document" => "missing_document",
        "re_upload_request" or "reupload_required" => "reupload_required",
        "renewal_request" or "compliance_renewal" => "compliance_renewal",
        _ => "clarification_needed"
    };

    private static string DescribeHistoryAction(string action) => action switch
    {
        "compliance.item_created" => "Compliance item created.",
        "compliance.item_updated" => "Compliance item updated.",
        "compliance.evidence_uploaded" => "A new compliance evidence version was uploaded.",
        "compliance.request_created" => "A compliance workflow request was created.",
        "compliance.reminder_created" => "A compliance reminder was scheduled.",
        "compliance.reminder_status_updated" => "A compliance reminder status changed.",
        "compliance.report_downloaded" => "A compliance report was downloaded.",
        _ => action.Replace("compliance.", string.Empty).Replace('_', ' ')
    };

    private static ServiceResult<T> ConvertError<T>(ServiceResult<object> error) =>
        new(
            Forbidden: error.Forbidden,
            NotFound: error.NotFound,
            Unauthorized: error.Unauthorized,
            Error: error.Error,
            ErrorCode: error.ErrorCode,
            StatusCode: error.StatusCode);

    private static string NormalizeStatus(string raw) => ComplianceDomainValues.ToComplianceItemStatus(raw).ToStorageValue();
    private static string NormalizeRiskLevel(string raw) => ComplianceDomainValues.ToComplianceRiskLevel(raw).ToStorageValue();

    private static object BuildComplianceItemPayload(ComplianceItem item, IReadOnlyDictionary<Guid, ComplianceCategory> categories, IReadOnlyDictionary<Guid, User> users)
    {
        var category = categories.GetValueOrDefault(item.CategoryId);
        var owner = item.OwnerUserId.HasValue ? users.GetValueOrDefault(item.OwnerUserId.Value) : null;
        var alertLevel = ComplianceAlertPolicy.ComputeAlertLevel(item, DateTime.UtcNow);

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
            alertLevel,
            item.CreatedAtUtc,
            item.UpdatedAtUtc
        };
    }

    private static object? BuildAlert(ComplianceItem item, IReadOnlyDictionary<Guid, ComplianceCategory> categories, IReadOnlyDictionary<Guid, User> users)
    {
        var alertLevel = ComplianceAlertPolicy.ComputeAlertLevel(item, DateTime.UtcNow);
        if (alertLevel is null)
        {
            return null;
        }

        var category = categories.GetValueOrDefault(item.CategoryId);
        var owner = item.OwnerUserId.HasValue ? users.GetValueOrDefault(item.OwnerUserId.Value) : null;

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
            message = ComplianceAlertPolicy.BuildAlertMessage(item, alertLevel)
        };
    }

    private static Guid DeterministicGuid(string value)
    {
        using var md5 = MD5.Create();
        var bytes = md5.ComputeHash(Encoding.UTF8.GetBytes($"secure-client-portal:{value}"));
        return new Guid(bytes);
    }
}
