using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application.Contracts;
using SecureClientPortal.Backend.Application.Requests;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;

namespace SecureClientPortal.Backend.Infrastructure.Requests.Application;

public sealed class RequestService : IRequestService
{
    private static readonly HashSet<string> AllowedRequestTypes =
    [
        "missing_document",
        "reupload_required",
        "clarification_needed",
        "signature_required",
        "compliance_renewal"
    ];

    private static readonly HashSet<string> AllowedStatuses =
    [
        "open",
        "waiting_on_client",
        "waiting_on_accountant",
        "resolved",
        "overdue"
    ];

    private readonly PortalDbContext _db;

    public RequestService(PortalDbContext db)
    {
        _db = db;
    }

    public async Task<(bool forbidden, IReadOnlyList<RequestItem> results)> GetAllAsync(ClaimsPrincipal user, CancellationToken ct = default)
    {
        await RefreshOverdueRequestsAsync(ct);
        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        var items = await _db.Requests
            .Where(x => allowedClientIds.Contains(x.ClientId))
            .OrderByDescending(x => x.RequestedAtUtc)
            .ToListAsync(ct);
        return (false, items);
    }

    public async Task<(bool forbidden, RequestItem? item)> GetByIdAsync(string id, ClaimsPrincipal user, CancellationToken ct = default)
    {
        await RefreshOverdueRequestsAsync(ct);
        var item = await _db.Requests.FindAsync([id], ct);
        if (item is null)
        {
            return (false, null);
        }

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        return allowedClientIds.Contains(item.ClientId) ? (false, item) : (true, null);
    }

    public async Task<(bool forbidden, RequestItem created)> CreateAsync(CreateRequestRequest request, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(request.ClientId))
        {
            return (true, null!);
        }

        var requestType = NormalizeRequestType(request.RequestType);
        if (!AllowedRequestTypes.Contains(requestType))
        {
            throw new ArgumentException("Unsupported request type.");
        }

        if (!IsAllowedPriority(request.Priority))
        {
            throw new ArgumentException("Priority must be low, medium, high, or urgent.");
        }

        if (!string.IsNullOrWhiteSpace(request.RelatedDocumentId))
        {
            var document = await _db.Documents.FirstOrDefaultAsync(x => x.Id == request.RelatedDocumentId, ct);
            if (document is null || !string.Equals(document.ClientId, request.ClientId, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Related document was not found for the selected client.");
            }
        }

        var authorId = user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? "unknown";
        var authorRole = user.IsAdmin() ? "admin" : user.IsAccountant() ? "accountant" : "client";
        var status = authorRole == "client" ? "waiting_on_accountant" : "waiting_on_client";

        var item = new RequestItem
        {
            Id = $"req_{Guid.NewGuid():N}",
            ClientId = request.ClientId,
            RequestType = requestType,
            RelatedDocumentId = request.RelatedDocumentId,
            Title = request.Title.Trim(),
            Description = request.Description.Trim(),
            Priority = request.Priority.Trim().ToLowerInvariant(),
            Status = status,
            DueDateUtc = request.DueDateUtc,
            RequestedByUserId = authorId,
            RequestedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _db.Requests.Add(item);
        await _db.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(
            user,
            "request.created",
            "request",
            item.Id,
            item.ClientId,
            JsonSerializer.Serialize(new { item.RequestType, item.Priority, item.Status, item.RelatedDocumentId }),
            ct);

        var notificationAudience = authorRole == "client" ? "accountant" : "client";
        var recipients = await _db.ResolveNotificationRecipientsAsync(item.ClientId, notificationAudience, ct);
        await _db.AddNotificationsAsync(
            user,
            recipients,
            item.ClientId,
            "request.created",
            "New workflow request",
            $"A {item.RequestType.Replace('_', ' ')} request was created for '{item.Title}'.",
            $"/requests/{item.Id}",
            new { item.Id, item.RequestType },
            ct);

        return (false, item);
    }

    public async Task<(bool forbidden, RequestItem? updated)> UpdateAsync(string id, RequestItem request, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var item = await _db.Requests.FindAsync([id], ct);
        if (item is null)
        {
            return (false, null);
        }

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(item.ClientId))
        {
            return (true, null);
        }

        var normalizedStatus = NormalizeStatus(request.Status);
        if (!AllowedStatuses.Contains(normalizedStatus))
        {
            throw new ArgumentException("Unsupported request status.");
        }

        var normalizedType = NormalizeRequestType(request.RequestType);
        if (!AllowedRequestTypes.Contains(normalizedType))
        {
            throw new ArgumentException("Unsupported request type.");
        }

        item.Title = request.Title.Trim();
        item.Description = request.Description.Trim();
        item.Priority = request.Priority.Trim().ToLowerInvariant();
        item.Status = normalizedStatus;
        item.DueDateUtc = request.DueDateUtc;
        item.RequestType = normalizedType;
        item.RelatedDocumentId = request.RelatedDocumentId;
        item.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(
            user,
            "request.updated",
            "request",
            item.Id,
            item.ClientId,
            JsonSerializer.Serialize(new { item.Status, item.Priority, item.RequestType }),
            ct);

        return (false, item);
    }

    public async Task<(bool forbidden, RequestItem? updated)> UpdateStatusAsync(string id, UpdateRequestStatusRequest request, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var item = await _db.Requests.FindAsync([id], ct);
        if (item is null)
        {
            return (false, null);
        }

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(item.ClientId))
        {
            return (true, null);
        }

        var normalizedStatus = NormalizeStatus(request.Status);
        if (!AllowedStatuses.Contains(normalizedStatus))
        {
            throw new ArgumentException("Unsupported request status.");
        }

        item.Status = normalizedStatus;
        item.UpdatedAtUtc = DateTime.UtcNow;

        if (normalizedStatus == "resolved")
        {
            item.ResolvedByUserId = user.GetUserId();
            item.ResolvedAtUtc = item.UpdatedAtUtc;
        }
        else
        {
            item.ResolvedByUserId = null;
            item.ResolvedAtUtc = null;
        }

        await _db.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(
            user,
            "request.status_updated",
            "request",
            item.Id,
            item.ClientId,
            JsonSerializer.Serialize(new { item.Status }),
            ct);

        return (false, item);
    }

    public async Task<(bool forbidden, IReadOnlyList<RequestComment>? comments)> GetCommentsAsync(string id, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var item = await _db.Requests.FindAsync([id], ct);
        if (item is null)
        {
            return (false, null);
        }

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(item.ClientId))
        {
            return (true, null);
        }

        var comments = await _db.RequestComments
            .Where(x => x.RequestId == item.Id)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(ct);
        return (false, comments);
    }

    public async Task<(bool forbidden, RequestComment? comment)> AddCommentAsync(string id, AddRequestCommentRequest request, ClaimsPrincipal user, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            throw new ArgumentException("Comment message is required.");
        }

        var item = await _db.Requests.FindAsync([id], ct);
        if (item is null)
        {
            return (false, null);
        }

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(item.ClientId))
        {
            return (true, null);
        }

        var authorId = user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? "unknown";
        var authorRole = user.IsAdmin() ? "admin" : user.IsAccountant() ? "accountant" : "client";
        var comment = new RequestComment
        {
            Id = $"rc_{Guid.NewGuid():N}",
            RequestId = item.Id,
            ClientId = item.ClientId,
            AuthorUserId = authorId,
            AuthorRole = authorRole,
            Message = request.Message.Trim(),
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.RequestComments.Add(comment);

        item.Status = authorRole == "client" ? "waiting_on_accountant" : "waiting_on_client";
        item.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(
            user,
            "comment.added",
            "request",
            item.Id,
            item.ClientId,
            JsonSerializer.Serialize(new { comment.Id, comment.AuthorRole, target = "request" }),
            ct);

        var recipientRole = authorRole == "client" ? "accountant" : "client";
        var recipientIds = await _db.ResolveNotificationRecipientsAsync(item.ClientId, recipientRole, ct);
        await _db.AddNotificationsAsync(
            user,
            recipientIds,
            item.ClientId,
            "request.comment",
            "Request replied to",
            $"New comment on request '{item.Title}'.",
            $"/requests/{item.Id}",
            new { requestId = item.Id, commentId = comment.Id },
            ct);

        return (false, comment);
    }

    public async Task<(bool forbidden, RequestItem? resolved)> ResolveAsync(string id, ResolveRequestRequest request, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var item = await _db.Requests.FindAsync([id], ct);
        if (item is null)
        {
            return (false, null);
        }

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(item.ClientId))
        {
            return (true, null);
        }

        item.Status = "resolved";
        item.ResolvedByUserId = user.GetUserId();
        item.ResolvedAtUtc = DateTime.UtcNow;
        item.UpdatedAtUtc = item.ResolvedAtUtc.Value;
        await _db.SaveChangesAsync(ct);

        await _db.WriteAuditLogAsync(
            user,
            "request.resolved",
            "request",
            item.Id,
            item.ClientId,
            JsonSerializer.Serialize(new { item.RequestType, request.ResolutionNote, item.RelatedDocumentId }),
            ct);

        var recipients = await _db.ResolveNotificationRecipientsAsync(item.ClientId, "client", ct);
        await _db.AddNotificationsAsync(
            user,
            recipients,
            item.ClientId,
            "request.resolved",
            "Request resolved",
            $"Request '{item.Title}' has been resolved.",
            $"/requests/{item.Id}",
            new { requestId = item.Id, request.ResolutionNote },
            ct);

        return (false, item);
    }

    public async Task<(bool forbidden, bool deleted)> DeleteAsync(string id, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var item = await _db.Requests.FindAsync([id], ct);
        if (item is null)
        {
            return (false, false);
        }

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(item.ClientId))
        {
            return (true, false);
        }

        _db.Requests.Remove(item);
        await _db.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(user, "request.deleted", "request", item.Id, item.ClientId, null, ct);
        return (false, true);
    }

    private static string NormalizeRequestType(string value)
    {
        var normalized = value.Trim().ToLowerInvariant().Replace("-", "_").Replace(" ", "_");
        return normalized switch
        {
            "reupload" => "reupload_required",
            "re_upload" => "reupload_required",
            "clarification" => "clarification_needed",
            "signature" => "signature_required",
            "renewal" => "compliance_renewal",
            _ => normalized
        };
    }

    private static string NormalizeStatus(string value)
    {
        var normalized = value.Trim().ToLowerInvariant().Replace("-", "_").Replace(" ", "_");
        return normalized switch
        {
            "awaiting_client" => "waiting_on_client",
            "awaiting_accountant" => "waiting_on_accountant",
            _ => normalized
        };
    }

    private static bool IsAllowedPriority(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized is "low" or "medium" or "high" or "urgent";
    }

    private async Task RefreshOverdueRequestsAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var overdueRequests = await _db.Requests
            .Where(x =>
                x.Status != "resolved" &&
                x.DueDateUtc != null &&
                x.DueDateUtc < now &&
                x.Status != "overdue")
            .ToListAsync(ct);

        if (overdueRequests.Count == 0)
        {
            return;
        }

        foreach (var item in overdueRequests)
        {
            item.Status = "overdue";
            item.UpdatedAtUtc = now;
        }

        await _db.SaveChangesAsync(ct);
    }
}
