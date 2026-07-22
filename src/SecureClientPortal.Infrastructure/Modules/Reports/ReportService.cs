using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application.Modules.Reports;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Domain.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Models;
using System.Security.Claims;

namespace SecureClientPortal.Backend.Infrastructure.Modules.Reports;

public sealed class ReportService : IReportService
{
    private readonly PortalDbContext _db;

    public ReportService(PortalDbContext db)
    {
        _db = db;
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
