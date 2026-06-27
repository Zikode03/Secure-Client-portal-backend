using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application;
using SecureClientPortal.Backend.Application.Common.Events;
using SecureClientPortal.Backend.Application.Platform;
using SecureClientPortal.Backend.Application.Requests;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using System.Text.Json;

namespace SecureClientPortal.Backend.Infrastructure.Requests.Application;

public sealed class RequestCommandService : IRequestCommandService
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

    private readonly IRequestModuleDbContext _requests;
    private readonly PortalDbContext _db;
    private readonly ICurrentUserContextFactory _currentUserContextFactory;
    private readonly IDomainEventDispatcher _domainEventDispatcher;

    public RequestCommandService(
        IRequestModuleDbContext requests,
        PortalDbContext db,
        ICurrentUserContextFactory currentUserContextFactory,
        IDomainEventDispatcher domainEventDispatcher)
    {
        _requests = requests;
        _db = db;
        _currentUserContextFactory = currentUserContextFactory;
        _domainEventDispatcher = domainEventDispatcher;
    }

    public async Task<(bool forbidden, RequestItem created)> CreateAsync(CreateRequestRequest request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default)
    {
        RequestValidators.ValidateCreate(request);

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(request.ClientId))
        {
            return (true, null!);
        }

        var requestType = RequestDomainValues.NormalizeRequestType(request.RequestType);
        if (!AllowedRequestTypes.Contains(requestType))
        {
            throw new AppValidationException("Unsupported request type.");
        }

        if (request.RelatedDocumentId.HasValue)
        {
            var document = await _requests.Documents.FirstOrDefaultAsync(x => x.Id == request.RelatedDocumentId.Value, ct);
            if (document is null || document.ClientId != request.ClientId)
            {
                throw new AppValidationException("Related document was not found for the selected client.");
            }
        }

        var currentUser = _currentUserContextFactory.Create(user);
        var authorId = currentUser.UserId ?? throw new InvalidOperationException("Authenticated user id is required.");
        var authorRole = currentUser.IsAdmin ? "admin" : currentUser.IsAccountant ? "accountant" : "client";
        var actor = currentUser.ToWorkflowActorContext();
        var status = RequestWorkflowPolicy.DetermineInitialStatus(actor);

        var item = RequestItem.Create(
            Guid.NewGuid(),
            request.ClientId,
            requestType,
            request.RelatedDocumentId,
            request.Title,
            request.Description,
            RequestDomainValues.ToRequestPriority(request.Priority),
            authorId,
            status,
            request.DueDateUtc);
        item.RecordCreated(actor);

        _requests.Requests.Add(item);
        await _requests.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(
            user,
            "request.created",
            "request",
            item.Id,
            item.ClientId,
            JsonSerializer.Serialize(new { item.RequestType, item.Priority, item.Status, item.RelatedDocumentId, authorRole }),
            ct);
        await _domainEventDispatcher.DispatchAsync(item.DequeueDomainEvents(), ct);

        return (false, item);
    }

    public async Task<(bool forbidden, RequestItem? updated)> UpdateAsync(string id, UpdateRequestRequest request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default)
    {
        RequestValidators.ValidateUpdate(request);

        if (!Guid.TryParse(id, out var requestId))
        {
            return (false, null);
        }

        var item = await _requests.Requests.FindAsync([requestId], ct);
        if (item is null)
        {
            return (false, null);
        }

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(item.ClientId))
        {
            return (true, null);
        }

        var normalizedStatus = RequestWorkflowPolicy.NormalizeExternalStatus(request.Status);
        if (!AllowedStatuses.Contains(normalizedStatus))
        {
            throw new AppValidationException("Unsupported request status.");
        }

        var normalizedType = RequestDomainValues.NormalizeRequestType(request.RequestType);
        if (!AllowedRequestTypes.Contains(normalizedType))
        {
            throw new AppValidationException("Unsupported request type.");
        }

        if (request.RelatedDocumentId.HasValue)
        {
            var document = await _requests.Documents.FirstOrDefaultAsync(x => x.Id == request.RelatedDocumentId.Value, ct);
            if (document is null || document.ClientId != item.ClientId)
            {
                throw new AppValidationException("Related document was not found for the selected client.");
            }
        }

        item.UpdateDetails(
            normalizedType,
            request.RelatedDocumentId,
            request.Title,
            request.Description,
            RequestDomainValues.ToRequestPriority(request.Priority),
            request.DueDateUtc);

        var status = RequestDomainValues.ToRequestStatus(normalizedStatus);
        if (status == RequestStatus.Resolved)
        {
            item.Resolve(_currentUserContextFactory.Create(user).UserId ?? throw new InvalidOperationException("Authenticated user id is required."));
        }
        else
        {
            item.SetStatus(status);
        }

        await _requests.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(
            user,
            "request.updated",
            "request",
            item.Id,
            item.ClientId,
            JsonSerializer.Serialize(new { item.Status, item.Priority, item.RequestType }),
            ct);
        await _domainEventDispatcher.DispatchAsync(item.DequeueDomainEvents(), ct);

        return (false, item);
    }

    public async Task<(bool forbidden, RequestItem? updated)> UpdateStatusAsync(string id, UpdateRequestStatusRequest request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default)
    {
        RequestValidators.ValidateStatusUpdate(request);

        if (!Guid.TryParse(id, out var requestId))
        {
            return (false, null);
        }

        var item = await _requests.Requests.FindAsync([requestId], ct);
        if (item is null)
        {
            return (false, null);
        }

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(item.ClientId))
        {
            return (true, null);
        }

        var normalizedStatus = RequestWorkflowPolicy.NormalizeExternalStatus(request.Status);
        if (!AllowedStatuses.Contains(normalizedStatus))
        {
            throw new AppValidationException("Unsupported request status.");
        }

        var status = RequestDomainValues.ToRequestStatus(normalizedStatus);
        if (status == RequestStatus.Resolved)
        {
            item.Resolve(_currentUserContextFactory.Create(user).UserId ?? throw new InvalidOperationException("Authenticated user id is required."));
        }
        else
        {
            item.SetStatus(status);
        }

        await _requests.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(
            user,
            "request.status_updated",
            "request",
            item.Id,
            item.ClientId,
            JsonSerializer.Serialize(new { item.Status }),
            ct);
        await _domainEventDispatcher.DispatchAsync(item.DequeueDomainEvents(), ct);

        return (false, item);
    }

    public async Task<(bool forbidden, RequestComment? comment)> AddCommentAsync(string id, AddRequestCommentRequest request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default)
    {
        RequestValidators.ValidateComment(request);

        if (!Guid.TryParse(id, out var requestId))
        {
            return (false, null);
        }

        var item = await _requests.Requests.FindAsync([requestId], ct);
        if (item is null)
        {
            return (false, null);
        }

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(item.ClientId))
        {
            return (true, null);
        }

        var currentUser = _currentUserContextFactory.Create(user);
        var authorId = currentUser.UserId ?? throw new InvalidOperationException("Authenticated user id is required.");
        var authorRole = currentUser.IsAdmin ? "admin" : currentUser.IsAccountant ? "accountant" : "client";
        var comment = RequestComment.Create(Guid.NewGuid(), item.Id, item.ClientId, authorId, authorRole, request.Message);
        _requests.RequestComments.Add(comment);

        RequestWorkflowPolicy.ApplyCommentTransition(item, currentUser.ToWorkflowActorContext());

        await _requests.SaveChangesAsync(ct);
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

    public async Task<(bool forbidden, RequestItem? resolved)> ResolveAsync(string id, ResolveRequestRequest request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default)
    {
        RequestValidators.ValidateResolve(request);

        if (!Guid.TryParse(id, out var requestId))
        {
            return (false, null);
        }

        var item = await _requests.Requests.FindAsync([requestId], ct);
        if (item is null)
        {
            return (false, null);
        }

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(item.ClientId))
        {
            return (true, null);
        }

        item.Resolve(_currentUserContextFactory.Create(user).UserId ?? throw new InvalidOperationException("Authenticated user id is required."));
        await _requests.SaveChangesAsync(ct);

        await _db.WriteAuditLogAsync(
            user,
            "request.resolved",
            "request",
            item.Id,
            item.ClientId,
            JsonSerializer.Serialize(new { item.RequestType, request.ResolutionNote, item.RelatedDocumentId }),
            ct);
        await _domainEventDispatcher.DispatchAsync(item.DequeueDomainEvents(), ct);

        return (false, item);
    }

    public async Task<(bool forbidden, bool deleted)> DeleteAsync(string id, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default)
    {
        if (!Guid.TryParse(id, out var requestId))
        {
            return (false, false);
        }

        var item = await _requests.Requests.FindAsync([requestId], ct);
        if (item is null)
        {
            return (false, false);
        }

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(item.ClientId))
        {
            return (true, false);
        }

        _requests.Requests.Remove(item);
        await _requests.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(user, "request.deleted", "request", item.Id, item.ClientId, null, ct);
        return (false, true);
    }
}
