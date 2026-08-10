using Microsoft.EntityFrameworkCore;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Fonts;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts.Modules.Reports;
using SecureClientPortal.Backend.Application.Modules.Reports;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Domain.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Models;
using System.Security.Claims;
using System.Text.Json;

namespace SecureClientPortal.Backend.Infrastructure.Modules.Reports;

public sealed class ReportService : IReportService
{
    private readonly PortalDbContext _db;

    public ReportService(PortalDbContext db)
    {
        _db = db;
    }

    static ReportService()
    {
        if (OperatingSystem.IsWindows())
        {
            GlobalFontSettings.UseWindowsFontsUnderWindows = true;
        }
    }

    public async Task<(bool forbidden, object? report)> GetFirmReportsAsync(ClaimsPrincipal user, CancellationToken ct = default)
    {
        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);

        var clients = await _db.Clients.Where(x => allowedClientIds.Contains(x.Id)).ToListAsync(ct);
        var documents = await _db.Documents.Where(x => allowedClientIds.Contains(x.ClientId)).ToListAsync(ct);
        var requests = await _db.Requests.Where(x => allowedClientIds.Contains(x.ClientId)).ToListAsync(ct);
        var complianceItems = await _db.ComplianceItems.Where(x => allowedClientIds.Contains(x.ClientId)).ToListAsync(ct);

        var overdueClients = clients
            .Where(client =>
                requests.Any(request => request.ClientId == client.Id && request.Status != "resolved" && request.DueDateUtc < DateTime.UtcNow) ||
                complianceItems.Any(item => item.ClientId == client.Id && item.Status is "expired" or "rejected"))
            .Select(client => new
            {
                client.Id,
                client.Name,
                openOverdueRequests = requests.Count(request => request.ClientId == client.Id && request.Status != "resolved" && request.DueDateUtc < DateTime.UtcNow),
                criticalComplianceItems = complianceItems.Count(item => item.ClientId == client.Id && item.Status is "expired" or "rejected")
            })
            .OrderByDescending(x => x.openOverdueRequests)
            .ThenByDescending(x => x.criticalComplianceItems)
            .ToList();

        var missingDocuments = clients
            .Select(client => new
            {
                client.Id,
                client.Name,
                missingRequiredItems = complianceItems.Count(item =>
                    item.ClientId == client.Id &&
                    item.RequiredDocumentCategory != null &&
                    item.LinkedDocumentId == null)
            })
            .Where(x => x.missingRequiredItems > 0)
            .OrderByDescending(x => x.missingRequiredItems)
            .ToList();

        var openRequests = requests
            .Where(x => x.Status != "resolved")
            .GroupBy(x => x.RequestType)
            .Select(group => new
            {
                requestType = group.Key,
                total = group.Count(),
                awaitingClient = group.Count(x => x.Status == "waiting_on_client"),
                awaitingAccountant = group.Count(x => x.Status == "waiting_on_accountant"),
                overdue = group.Count(x => x.Status == "overdue" || (x.Status != "resolved" && x.DueDateUtc < DateTime.UtcNow))
            })
            .OrderByDescending(x => x.total)
            .ToList();

        var complianceRisk = clients
            .Select(client =>
            {
                var clientItems = complianceItems.Where(item => item.ClientId == client.Id).ToList();
                return new
                {
                    client.Id,
                    client.Name,
                    complianceScore = CalculateComplianceScore(clientItems),
                    expired = clientItems.Count(item => item.Status == "expired"),
                    highRisk = clientItems.Count(item => item.RiskLevel is "high" or "critical"),
                    missing = clientItems.Count(item => item.Status == "missing")
                };
            })
            .OrderBy(x => x.complianceScore)
            .ToList();

        return (false, new
        {
            generatedAtUtc = DateTime.UtcNow,
            overdueClients,
            missingDocuments,
            openRequests,
            complianceRisk,
            totals = new
            {
                totalClients = clients.Count,
                totalOpenRequests = requests.Count(x => x.Status != "resolved"),
                totalMissingDocuments = missingDocuments.Sum(x => x.missingRequiredItems),
                totalHighRiskComplianceItems = complianceItems.Count(x => x.RiskLevel is "high" or "critical")
            }
        });
    }

    public async Task<(bool forbidden, object? report)> GetOperationsDashboardAsync(ClaimsPrincipal user, CancellationToken ct = default)
    {
        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        var now = DateTime.UtcNow;

        var clients = await _db.Clients
            .Where(x => allowedClientIds.Contains(x.Id))
            .ToListAsync(ct);
        var clientLookup = clients.ToDictionary(x => x.Id);

        var packs = await _db.MonthlyPacks
            .Where(x => allowedClientIds.Contains(x.ClientId))
            .ToListAsync(ct);
        var slots = await _db.DocumentSlots
            .Where(x => allowedClientIds.Contains(x.ClientId))
            .ToListAsync(ct);
        var requests = await _db.Requests
            .Where(x => allowedClientIds.Contains(x.ClientId))
            .ToListAsync(ct);
        var complianceItems = await _db.ComplianceItems
            .Where(x => allowedClientIds.Contains(x.ClientId))
            .ToListAsync(ct);
        var documents = await _db.Documents
            .Where(x => allowedClientIds.Contains(x.ClientId))
            .ToListAsync(ct);
        var assignments = await _db.ClientAssignments
            .Where(x => allowedClientIds.Contains(x.ClientId))
            .ToListAsync(ct);
        var accountants = await _db.Users
            .Where(x => x.Role == "accountant")
            .ToDictionaryAsync(x => x.Id, ct);

        var reviewQueueItems = from slot in slots
                               where slot.Status is "submitted" or "under_review"
                               join pack in packs on slot.MonthlyPackId equals pack.Id
                               join client in clients on slot.ClientId equals client.Id
                               select new
                               {
                                   slot.Id,
                                   slot.ClientId,
                                   client.Name,
                                   slot.Category,
                                   slot.Status,
                                   ReviewAgeDays = Math.Max(0, (int)Math.Floor((now - (slot.SubmittedAtUtc ?? slot.UpdatedAtUtc)).TotalDays))
                               };

        var reviewQueueList = reviewQueueItems.ToList();
        var queueDashboard = new
        {
            total = reviewQueueList.Count,
            submitted = reviewQueueList.Count(x => x.Status == "submitted"),
            underReview = reviewQueueList.Count(x => x.Status == "under_review"),
            urgent = reviewQueueList.Count(x => x.ReviewAgeDays >= 7),
            high = reviewQueueList.Count(x => x.ReviewAgeDays >= 3 && x.ReviewAgeDays < 7),
            oldestAgeDays = reviewQueueList.Count == 0 ? 0 : reviewQueueList.Max(x => x.ReviewAgeDays),
            byClient = reviewQueueList
                .GroupBy(x => new { x.ClientId, x.Name })
                .Select(group => new
                {
                    clientId = group.Key.ClientId,
                    clientName = group.Key.Name,
                    items = group.Count(),
                    urgent = group.Count(x => x.ReviewAgeDays >= 7),
                    oldestAgeDays = group.Max(x => x.ReviewAgeDays)
                })
                .OrderByDescending(x => x.urgent)
                .ThenByDescending(x => x.items)
                .ThenByDescending(x => x.oldestAgeDays)
                .Take(10)
                .ToList()
        };

        var monthlyPackDeadlineDay = 28;
        var openPacks = packs.Where(x => x.Status != "closed").ToList();
        var packDashboard = new
        {
            totalOpen = openPacks.Count,
            readyToClose = openPacks.Count(pack =>
            {
                var packSlots = slots.Where(x => x.MonthlyPackId == pack.Id).ToList();
                return packSlots.Count > 0 && packSlots.Where(x => x.IsRequired && x.Status != "not_applicable").All(x => x.Status == "accepted");
            }),
            missingRequiredSlots = openPacks.Count(pack =>
                slots.Any(x => x.MonthlyPackId == pack.Id && x.IsRequired && x.Status is "not_started" or "draft" or "reupload_required" or "rejected")),
            overdueOpen = openPacks.Count(pack =>
            {
                var dueDate = BuildMonthDueDate(pack.Year, pack.Month, monthlyPackDeadlineDay);
                return dueDate < now.Date && pack.Status != "closed" && pack.Status != "complete";
            }),
            byStatus = openPacks
                .GroupBy(x => x.Status)
                .Select(group => new { status = group.Key, total = group.Count() })
                .OrderByDescending(x => x.total)
                .ToList()
        };

        var openRequests = requests.Where(x => x.Status != "resolved").ToList();
        var requestDashboard = new
        {
            totalOpen = openRequests.Count,
            waitingOnClient = openRequests.Count(x => x.Status == "waiting_on_client"),
            waitingOnAccountant = openRequests.Count(x => x.Status == "waiting_on_accountant"),
            overdue = openRequests.Count(x => x.Status == "overdue" || (x.DueDateUtc.HasValue && x.DueDateUtc.Value < now)),
            averageAgeDays = openRequests.Count == 0 ? 0 : Math.Round(openRequests.Average(x => (now - x.RequestedAtUtc).TotalDays), 2),
            byClient = openRequests
                .GroupBy(x => x.ClientId)
                .Select(group => new
                {
                    clientId = group.Key,
                    clientName = clientLookup.GetValueOrDefault(group.Key)?.Name,
                    total = group.Count(),
                    overdue = group.Count(x => x.Status == "overdue" || (x.DueDateUtc.HasValue && x.DueDateUtc.Value < now)),
                    waitingOnClient = group.Count(x => x.Status == "waiting_on_client")
                })
                .OrderByDescending(x => x.overdue)
                .ThenByDescending(x => x.total)
                .Take(10)
                .ToList()
        };

        var complianceDashboard = new
        {
            expired = complianceItems.Count(x => x.Status == "expired"),
            expiringSoon = complianceItems.Count(x => x.Status == "expiring_soon"),
            rejected = complianceItems.Count(x => x.Status == "rejected"),
            missing = complianceItems.Count(x => x.Status == "missing"),
            criticalRisk = complianceItems.Count(x => x.RiskLevel == "critical"),
            byClient = complianceItems
                .GroupBy(x => x.ClientId)
                .Select(group => new
                {
                    clientId = group.Key,
                    clientName = clientLookup.GetValueOrDefault(group.Key)?.Name,
                    expired = group.Count(x => x.Status == "expired"),
                    expiringSoon = group.Count(x => x.Status == "expiring_soon"),
                    criticalRisk = group.Count(x => x.RiskLevel == "critical")
                })
                .Where(x => x.expired > 0 || x.expiringSoon > 0 || x.criticalRisk > 0)
                .OrderByDescending(x => x.expired)
                .ThenByDescending(x => x.criticalRisk)
                .ThenByDescending(x => x.expiringSoon)
                .Take(10)
                .ToList()
        };

        var workloadDashboard = assignments
            .GroupBy(x => x.AccountantUserId)
            .Select(group =>
            {
                var assignedClientIds = group.Select(x => x.ClientId).Distinct().ToHashSet();
                return new
                {
                    accountantUserId = group.Key,
                    accountantName = accountants.GetValueOrDefault(group.Key)?.FullName,
                    assignedClients = assignedClientIds.Count,
                    pendingReviewItems = reviewQueueList.Count(x => assignedClientIds.Contains(x.ClientId)),
                    openRequests = openRequests.Count(x => assignedClientIds.Contains(x.ClientId)),
                    expiredCompliance = complianceItems.Count(x => assignedClientIds.Contains(x.ClientId) && x.Status == "expired")
                };
            })
            .OrderByDescending(x => x.pendingReviewItems)
            .ThenByDescending(x => x.openRequests)
            .ThenByDescending(x => x.expiredCompliance)
            .ThenBy(x => x.accountantName)
            .ToList();

        return (false, new
        {
            generatedAtUtc = now,
            reviewQueue = queueDashboard,
            monthlyPacks = packDashboard,
            requests = requestDashboard,
            compliance = complianceDashboard,
            workload = workloadDashboard
        });
    }

    public async Task<object> GetAccountantReportsAsync(CancellationToken ct = default)
    {
        var users = await _db.Users.Where(x => x.Role == "accountant").ToListAsync(ct);
        var assignments = await _db.ClientAssignments.ToListAsync(ct);
        var tasks = await _db.Tasks.ToListAsync(ct);
        var documents = await _db.Documents.ToListAsync(ct);
        var reviews = await _db.ReviewDecisions.ToListAsync(ct);
        var requests = await _db.Requests.ToListAsync(ct);

        var report = users
            .Select(accountant =>
            {
                var assignedClientIds = assignments
                    .Where(x => x.AccountantUserId == accountant.Id)
                    .Select(x => x.ClientId)
                    .Distinct()
                    .ToHashSet();

                var assignedDocuments = documents.Where(x => assignedClientIds.Contains(x.ClientId)).ToList();
                var assignedReviews = reviews.Where(x => x.ReviewerUserId == accountant.Id).ToList();
                var reviewTimesInHours = assignedReviews
                    .Join(
                        assignedDocuments,
                        review => review.DocumentId,
                        document => document.Id,
                        (review, document) => (review.DecidedAtUtc - document.UploadedAtUtc).TotalHours)
                    .Where(hours => hours >= 0)
                    .ToList();

                return new
                {
                    accountantUserId = accountant.Id,
                    accountantName = accountant.FullName,
                    assignedClients = assignedClientIds.Count,
                    workload = new
                    {
                        openTasks = tasks.Count(task => task.CreatedByUserId == accountant.Id && task.Status != "done"),
                        pendingDocuments = assignedDocuments.Count(document => document.Status is "uploaded" or "under_review"),
                        openRequests = requests.Count(request => assignedClientIds.Contains(request.ClientId) && request.Status != "resolved")
                    },
                    reviewTime = new
                    {
                        averageHours = reviewTimesInHours.Count == 0 ? 0 : Math.Round(reviewTimesInHours.Average(), 2),
                        totalReviews = assignedReviews.Count
                    }
                };
            })
            .OrderByDescending(x => x.assignedClients)
            .ThenBy(x => x.accountantName)
            .ToList();

        return new
        {
            generatedAtUtc = DateTime.UtcNow,
            accountants = report
        };
    }

    public async Task<object> GetClientReportsAsync(ClaimsPrincipal user, CancellationToken ct = default)
    {
        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);

        var clients = await _db.Clients.Where(x => allowedClientIds.Contains(x.Id)).ToListAsync(ct);
        var packs = await _db.MonthlyPacks.Where(x => allowedClientIds.Contains(x.ClientId)).ToListAsync(ct);
        var requests = await _db.Requests.Where(x => allowedClientIds.Contains(x.ClientId)).ToListAsync(ct);
        var complianceItems = await _db.ComplianceItems.Where(x => allowedClientIds.Contains(x.ClientId)).ToListAsync(ct);

        var report = clients
            .Select(client =>
            {
                var clientPacks = packs.Where(x => x.ClientId == client.Id).ToList();
                var clientRequests = requests.Where(x => x.ClientId == client.Id).ToList();
                var clientComplianceItems = complianceItems.Where(x => x.ClientId == client.Id).ToList();

                return new
                {
                    client.Id,
                    client.Name,
                    complianceScore = CalculateComplianceScore(clientComplianceItems),
                    submissionRate = CalculateSubmissionRate(clientPacks),
                    outstandingItems = new
                    {
                        openRequests = clientRequests.Count(x => x.Status != "resolved"),
                        missingComplianceItems = clientComplianceItems.Count(x => x.Status == "missing"),
                        expiredComplianceItems = clientComplianceItems.Count(x => x.Status == "expired"),
                        rejectedComplianceItems = clientComplianceItems.Count(x => x.Status == "rejected")
                    }
                };
            })
            .OrderBy(x => x.Name)
            .ToList();

        return new
        {
            generatedAtUtc = DateTime.UtcNow,
            clients = report
        };
    }

    public async Task<ServiceResult<ReportFileResponse>> GenerateCompliancePdfAsync(
        ClaimsPrincipal user,
        string? clientId = null,
        CancellationToken ct = default)
    {
        var scope = await ResolveReportScopeAsync(user, clientId, ct);
        if (scope.Error is not null)
        {
            return scope.Error;
        }

        var clients = await _db.Clients
            .Where(x => scope.ClientIds.Contains(x.Id))
            .OrderBy(x => x.Name)
            .ToListAsync(ct);
        if (clients.Count == 0)
        {
            return ServiceResult<ReportFileResponse>.NotFoundResult("No accessible clients were found for this report.");
        }

        var items = await _db.ComplianceItems
            .Where(x => scope.ClientIds.Contains(x.ClientId))
            .OrderBy(x => x.Name)
            .ToListAsync(ct);
        var auditLogs = await _db.AuditLogs
            .Where(x => x.ClientId.HasValue && scope.ClientIds.Contains(x.ClientId.Value) && x.Action.StartsWith("compliance."))
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(50)
            .ToListAsync(ct);

        var generatedAtUtc = DateTime.UtcNow;
        var content = BuildCompliancePdf(clients, items, auditLogs, generatedAtUtc);
        var fileToken = clients.Count == 1
            ? SanitizeFileToken(clients[0].Name)
            : "portfolio";
        var response = new ReportFileResponse(
            content,
            "application/pdf",
            $"compliance-report-{fileToken}-{generatedAtUtc:yyyyMMdd-HHmm}.pdf");

        foreach (var client in clients)
        {
            await _db.WriteAuditLogAsync(
                user,
                "compliance.report_downloaded",
                "client",
                client.Id,
                client.Id,
                JsonSerializer.Serialize(new { generatedAtUtc, reportClientCount = clients.Count, response.FileName }),
                ct);
        }

        return ServiceResult<ReportFileResponse>.Success(response);
    }

    public async Task<ServiceResult<IReadOnlyList<ReportScheduleResponse>>> GetSchedulesAsync(
        ClaimsPrincipal user,
        string? clientId = null,
        CancellationToken ct = default)
    {
        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        var userId = user.GetUserId();
        if (!userId.HasValue)
        {
            return ServiceResult<IReadOnlyList<ReportScheduleResponse>>.UnauthorizedResult();
        }

        var query = _db.ReportSchedules.Where(x =>
            (user.IsAdmin() || x.CreatedByUserId == userId.Value) &&
            ((x.ClientId.HasValue && allowedClientIds.Contains(x.ClientId.Value)) ||
             (!x.ClientId.HasValue && x.CreatedByUserId == userId.Value)));
        if (!string.IsNullOrWhiteSpace(clientId))
        {
            if (!Guid.TryParse(clientId, out var parsedClientId))
            {
                return ServiceResult<IReadOnlyList<ReportScheduleResponse>>.ErrorResult("Client id is invalid.");
            }

            if (!allowedClientIds.Contains(parsedClientId))
            {
                return ServiceResult<IReadOnlyList<ReportScheduleResponse>>.ForbiddenResult();
            }

            query = query.Where(x => x.ClientId == parsedClientId);
        }

        var schedules = await query.OrderBy(x => x.NextRunAtUtc).ToListAsync(ct);
        return ServiceResult<IReadOnlyList<ReportScheduleResponse>>.Success(schedules.Select(MapSchedule).ToList());
    }

    public async Task<ServiceResult<ReportScheduleResponse>> CreateScheduleAsync(
        CreateReportScheduleRequest request,
        ClaimsPrincipal user,
        CancellationToken ct = default)
    {
        var userId = user.GetUserId();
        if (!userId.HasValue)
        {
            return ServiceResult<ReportScheduleResponse>.UnauthorizedResult();
        }

        var resolvedClient = await ResolveScheduleClientIdAsync(user, request.ClientId, ct);
        if (resolvedClient.Forbidden)
        {
            return ServiceResult<ReportScheduleResponse>.ForbiddenResult();
        }

        try
        {
            var schedule = ReportSchedule.Create(
                Guid.NewGuid(),
                userId.Value,
                resolvedClient.ClientId,
                request.Frequency,
                request.Recipients,
                DateTime.UtcNow);
            _db.ReportSchedules.Add(schedule);
            await _db.SaveChangesAsync(ct);
            await WriteScheduleAuditAsync(user, schedule, "reports.schedule_created", ct);
            return ServiceResult<ReportScheduleResponse>.Success(MapSchedule(schedule));
        }
        catch (DomainRuleException ex)
        {
            return ServiceResult<ReportScheduleResponse>.ErrorResult(ex.Message);
        }
    }

    public async Task<ServiceResult<ReportScheduleResponse>> UpdateScheduleAsync(
        string id,
        UpdateReportScheduleRequest request,
        ClaimsPrincipal user,
        CancellationToken ct = default)
    {
        var scheduleResult = await ResolveAccessibleScheduleAsync(id, user, ct);
        if (scheduleResult.Error is not null)
        {
            return scheduleResult.Error;
        }

        try
        {
            scheduleResult.Schedule!.Update(request.Frequency, request.Recipients, DateTime.UtcNow);
        }
        catch (DomainRuleException ex)
        {
            return ServiceResult<ReportScheduleResponse>.ErrorResult(ex.Message);
        }

        await _db.SaveChangesAsync(ct);
        await WriteScheduleAuditAsync(user, scheduleResult.Schedule, "reports.schedule_updated", ct);
        return ServiceResult<ReportScheduleResponse>.Success(MapSchedule(scheduleResult.Schedule));
    }

    public async Task<ServiceResult<bool>> DeleteScheduleAsync(
        string id,
        ClaimsPrincipal user,
        CancellationToken ct = default)
    {
        var scheduleResult = await ResolveAccessibleScheduleAsync(id, user, ct);
        if (scheduleResult.Error is not null)
        {
            return new ServiceResult<bool>(
                Forbidden: scheduleResult.Error.Forbidden,
                NotFound: scheduleResult.Error.NotFound,
                Unauthorized: scheduleResult.Error.Unauthorized,
                Error: scheduleResult.Error.Error,
                ErrorCode: scheduleResult.Error.ErrorCode,
                StatusCode: scheduleResult.Error.StatusCode);
        }

        var schedule = scheduleResult.Schedule!;
        _db.ReportSchedules.Remove(schedule);
        await _db.SaveChangesAsync(ct);
        await WriteScheduleAuditAsync(user, schedule, "reports.schedule_deleted", ct);
        return ServiceResult<bool>.Success(true);
    }

    private async Task<(HashSet<Guid> ClientIds, ServiceResult<ReportFileResponse>? Error)> ResolveReportScopeAsync(
        ClaimsPrincipal user,
        string? clientId,
        CancellationToken ct)
    {
        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return (allowedClientIds, null);
        }

        if (!Guid.TryParse(clientId, out var parsedClientId))
        {
            return ([], ServiceResult<ReportFileResponse>.ErrorResult("Client id is invalid."));
        }

        return allowedClientIds.Contains(parsedClientId)
            ? ([parsedClientId], null)
            : ([], ServiceResult<ReportFileResponse>.ForbiddenResult());
    }

    private async Task<(Guid? ClientId, bool Forbidden)> ResolveScheduleClientIdAsync(
        ClaimsPrincipal user,
        Guid? requestedClientId,
        CancellationToken ct)
    {
        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (requestedClientId.HasValue)
        {
            return allowedClientIds.Contains(requestedClientId.Value)
                ? (requestedClientId, false)
                : (null, true);
        }

        if (user.IsClient())
        {
            return allowedClientIds.Count == 1
                ? (allowedClientIds.Single(), false)
                : (null, true);
        }

        return (null, false);
    }

    private async Task<(ReportSchedule? Schedule, ServiceResult<ReportScheduleResponse>? Error)> ResolveAccessibleScheduleAsync(
        string id,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        if (!Guid.TryParse(id, out var scheduleId))
        {
            return (null, ServiceResult<ReportScheduleResponse>.NotFoundResult());
        }

        var schedule = await _db.ReportSchedules.FirstOrDefaultAsync(x => x.Id == scheduleId, ct);
        if (schedule is null)
        {
            return (null, ServiceResult<ReportScheduleResponse>.NotFoundResult());
        }

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        var userId = user.GetUserId();
        var ownsSchedule = user.IsAdmin() || (userId.HasValue && schedule.CreatedByUserId == userId.Value);
        var canAccess = ownsSchedule && (schedule.ClientId.HasValue
            ? allowedClientIds.Contains(schedule.ClientId.Value)
            : userId.HasValue && schedule.CreatedByUserId == userId.Value);
        return canAccess
            ? (schedule, null)
            : (null, ServiceResult<ReportScheduleResponse>.ForbiddenResult());
    }

    private async Task WriteScheduleAuditAsync(ClaimsPrincipal user, ReportSchedule schedule, string action, CancellationToken ct)
    {
        await _db.WriteAuditLogAsync(
            user,
            action,
            "report_schedule",
            schedule.Id,
            schedule.ClientId,
            JsonSerializer.Serialize(new { schedule.ReportType, schedule.Frequency, schedule.NextRunAtUtc, recipients = schedule.GetRecipients() }),
            ct);
    }

    private static ReportScheduleResponse MapSchedule(ReportSchedule schedule) =>
        new(
            schedule.Id,
            schedule.CreatedByUserId,
            schedule.ClientId,
            schedule.ReportType,
            schedule.Frequency,
            schedule.GetRecipients(),
            schedule.NextRunAtUtc,
            schedule.LastScheduledAtUtc,
            schedule.CreatedAtUtc,
            schedule.UpdatedAtUtc);

    private static byte[] BuildCompliancePdf(
        IReadOnlyCollection<Client> clients,
        IReadOnlyCollection<ComplianceItem> items,
        IReadOnlyCollection<AuditLog> auditLogs,
        DateTime generatedAtUtc)
    {
        var document = new MigraDoc.DocumentObjectModel.Document();
        document.Info.Title = "Compliance report";
        document.Info.Subject = "Current compliance register and controlled history";
        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = "Arial";
        normal.Font.Size = 9;

        var section = document.AddSection();
        section.PageSetup.TopMargin = Unit.FromCentimeter(1.5);
        section.PageSetup.BottomMargin = Unit.FromCentimeter(1.5);
        section.PageSetup.LeftMargin = Unit.FromCentimeter(1.5);
        section.PageSetup.RightMargin = Unit.FromCentimeter(1.5);

        var title = section.AddParagraph("Compliance Report");
        title.Format.Font.Size = 20;
        title.Format.Font.Bold = true;
        title.Format.Font.Color = Colors.DarkBlue;
        title.Format.SpaceAfter = Unit.FromPoint(4);
        var subtitle = section.AddParagraph($"Generated {generatedAtUtc:yyyy-MM-dd HH:mm} UTC | {clients.Count} client(s)");
        subtitle.Format.Font.Color = Colors.Gray;
        subtitle.Format.SpaceAfter = Unit.FromPoint(14);

        foreach (var client in clients)
        {
            var clientItems = items.Where(x => x.ClientId == client.Id).ToList();
            var heading = section.AddParagraph(client.Name);
            heading.Format.Font.Size = 13;
            heading.Format.Font.Bold = true;
            heading.Format.SpaceBefore = Unit.FromPoint(10);
            heading.Format.SpaceAfter = Unit.FromPoint(5);

            var summary = section.AddParagraph(
                $"Score: {CalculateComplianceScore(clientItems)}%   Valid: {clientItems.Count(x => x.Status == "valid")}   " +
                $"Expiring: {clientItems.Count(x => x.Status == "expiring_soon")}   Expired: {clientItems.Count(x => x.Status == "expired")}   " +
                $"Missing: {clientItems.Count(x => x.Status == "missing")}");
            summary.Format.SpaceAfter = Unit.FromPoint(6);

            var table = section.AddTable();
            table.Borders.Width = 0.5;
            table.Borders.Color = Colors.LightGray;
            table.AddColumn(Unit.FromCentimeter(7.2));
            table.AddColumn(Unit.FromCentimeter(3.2));
            table.AddColumn(Unit.FromCentimeter(3.0));
            table.AddColumn(Unit.FromCentimeter(3.0));
            var header = table.AddRow();
            header.Shading.Color = Colors.LightGray;
            header.Format.Font.Bold = true;
            header.Cells[0].AddParagraph("Item");
            header.Cells[1].AddParagraph("Status");
            header.Cells[2].AddParagraph("Risk");
            header.Cells[3].AddParagraph("Expiry");

            foreach (var item in clientItems)
            {
                var row = table.AddRow();
                row.Cells[0].AddParagraph(item.Name);
                row.Cells[1].AddParagraph(item.Status.Replace('_', ' '));
                row.Cells[2].AddParagraph(item.RiskLevel);
                row.Cells[3].AddParagraph(item.ExpiryDateUtc?.ToString("yyyy-MM-dd") ?? "-");
            }

            if (clientItems.Count == 0)
            {
                var row = table.AddRow();
                row.Cells[0].MergeRight = 3;
                row.Cells[0].AddParagraph("No compliance items are currently configured.");
            }
        }

        var auditHeading = section.AddParagraph("Recent controlled history");
        auditHeading.Format.Font.Size = 13;
        auditHeading.Format.Font.Bold = true;
        auditHeading.Format.SpaceBefore = Unit.FromPoint(16);
        auditHeading.Format.SpaceAfter = Unit.FromPoint(5);
        foreach (var log in auditLogs.Take(20))
        {
            section.AddParagraph($"{log.CreatedAtUtc:yyyy-MM-dd HH:mm} UTC | {log.Action} | {log.ActorRole}");
        }

        if (auditLogs.Count == 0)
        {
            section.AddParagraph("No compliance history has been recorded yet.");
        }

        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        using var stream = new MemoryStream();
        renderer.PdfDocument.Save(stream, false);
        return stream.ToArray();
    }

    private static string SanitizeFileToken(string value)
    {
        var chars = value.Trim().ToLowerInvariant().Select(x => char.IsLetterOrDigit(x) ? x : '-').ToArray();
        return new string(chars).Trim('-');
    }

    private static int CalculateComplianceScore(IReadOnlyCollection<ComplianceItem> items)
    {
        if (items.Count == 0)
        {
            return 0;
        }

        return (int)Math.Round((double)items.Count(x => x.Status == "valid") / items.Count * 100);
    }

    private static double CalculateSubmissionRate(IReadOnlyCollection<MonthlyPack> packs)
    {
        if (packs.Count == 0)
        {
            return 0;
        }

        var submittedCount = packs.Count(x => x.Status is "partially_submitted" or "under_review" or "complete" or "closed");
        return Math.Round((double)submittedCount / packs.Count * 100, 2);
    }

    private static DateTime BuildMonthDueDate(int year, int month, int dueDayOfMonth)
    {
        var safeDay = Math.Min(dueDayOfMonth, DateTime.DaysInMonth(year, month));
        return new DateTime(year, month, safeDay, 0, 0, 0, DateTimeKind.Utc);
    }
}
