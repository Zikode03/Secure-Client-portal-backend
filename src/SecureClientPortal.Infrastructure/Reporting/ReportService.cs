using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application.Reporting;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Models;
using System.Security.Claims;

namespace SecureClientPortal.Backend.Infrastructure.Reporting;

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
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

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

        var submittedCount = packs.Count(x => x.Status is "submitted" or "under_review" or "completed");
        return Math.Round((double)submittedCount / packs.Count * 100, 2);
    }
}
