using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Common.Events;
using SecureClientPortal.Backend.Application.Documents;
using SecureClientPortal.Backend.Application.Platform;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using System.Text.Json;

namespace SecureClientPortal.Backend.Infrastructure.Documents.Application;

public sealed class DocumentLifecycleService : IDocumentLifecycleService
{
    private readonly IDocumentModuleDbContext _documents;
    private readonly PortalDbContext _db;
    private readonly ICurrentUserContextFactory _currentUserContextFactory;
    private readonly IDomainEventDispatcher _domainEventDispatcher;

    public DocumentLifecycleService(
        IDocumentModuleDbContext documents,
        PortalDbContext db,
        ICurrentUserContextFactory currentUserContextFactory,
        IDomainEventDispatcher domainEventDispatcher)
    {
        _documents = documents;
        _db = db;
        _currentUserContextFactory = currentUserContextFactory;
        _domainEventDispatcher = domainEventDispatcher;
    }

    public async Task<ServiceResult<Document>> UpdateStatusAsync(string id, UpdateDocumentStatusRequest request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default)
    {
        if (!Guid.TryParse(id, out var documentId))
        {
            return ServiceResult<Document>.NotFoundResult();
        }

        var document = await _documents.Documents.FindAsync([documentId], ct);
        if (document is null)
        {
            return ServiceResult<Document>.NotFoundResult();
        }

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(document.ClientId))
        {
            return ServiceResult<Document>.ForbiddenResult();
        }

        await ApplyLifecycleDecisionAsync(document, NormalizeDocumentStatus(request.Status), null, null, user, ct);
        return ServiceResult<Document>.Success(document);
    }

    public async Task<ServiceResult<object>> ReviewAsync(string id, AddReviewDecisionRequest request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default)
    {
        DocumentValidators.ValidateReview(request);

        if (!Guid.TryParse(id, out var documentId))
        {
            return ServiceResult<object>.NotFoundResult();
        }

        var document = await _documents.Documents.FindAsync([documentId], ct);
        if (document is null)
        {
            return ServiceResult<object>.NotFoundResult();
        }

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(document.ClientId))
        {
            return ServiceResult<object>.ForbiddenResult();
        }

        var reviewDecision = await ApplyLifecycleDecisionAsync(document, request.Decision, request.Reason, request.InternalNote, user, ct);
        return ServiceResult<object>.Success(new
        {
            reviewDecision.Id,
            reviewDecision.Decision,
            reviewDecision.Reason,
            reviewDecision.InternalNote,
            reviewDecision.DecidedAtUtc,
            documentId = document.Id,
            documentStatus = document.Status
        });
    }

    public async Task<ServiceResult<object>> RequestReuploadAsync(string id, RequestReuploadRequest request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default)
    {
        DocumentValidators.ValidateReupload(request);

        if (!Guid.TryParse(id, out var documentId))
        {
            return ServiceResult<object>.NotFoundResult();
        }

        var document = await _documents.Documents.FindAsync([documentId], ct);
        if (document is null)
        {
            return ServiceResult<object>.NotFoundResult();
        }

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(document.ClientId))
        {
            return ServiceResult<object>.ForbiddenResult();
        }

        var reviewDecision = await ApplyLifecycleDecisionAsync(document, "request_reupload", request.Reason, request.InternalNote, user, ct);
        return ServiceResult<object>.Success(new
        {
            reviewDecision.Id,
            reviewDecision.Decision,
            reviewDecision.Reason,
            reviewDecision.InternalNote,
            reviewDecision.DecidedAtUtc,
            documentId = document.Id,
            documentStatus = document.Status
        });
    }

    private async Task<ReviewDecision> ApplyLifecycleDecisionAsync(Document document, string decision, string? reason, string? internalNote, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct)
    {
        var currentUser = _currentUserContextFactory.Create(user);
        var reviewerUserId = currentUser.UserId ?? throw new InvalidOperationException("Authenticated user id is required.");
        var reviewerRole = currentUser.IsAdmin ? "admin" : "accountant";
        var now = DateTime.UtcNow;
        var normalizedDecision = DocumentReviewPolicy.NormalizeDecision(decision);

        var slot = !document.DocumentSlotId.HasValue
            ? null
            : await _documents.DocumentSlots.FirstOrDefaultAsync(x => x.Id == document.DocumentSlotId.Value, ct);
        var pack = slot is null
            ? null
            : await _documents.MonthlyPacks.FirstOrDefaultAsync(x => x.Id == slot.MonthlyPackId, ct);

        var reviewDecision = DocumentReviewPolicy.ApplyDecision(
            document,
            slot,
            pack,
            normalizedDecision,
            reviewerUserId,
            reviewerRole,
            reason,
            internalNote,
            now);

        document.RecordReviewDecision(normalizedDecision, reviewDecision.Reason, reviewerUserId, reviewerRole, now);
        _documents.ReviewDecisions.Add(reviewDecision);

        if (pack is not null)
        {
            var slots = await _documents.DocumentSlots.Where(x => x.MonthlyPackId == pack.Id).ToListAsync(ct);
            MonthlyPackStatusPolicy.Recalculate(pack, slots);
        }

        await _documents.SaveChangesAsync(ct);

        var action = normalizedDecision switch
        {
            "under_review" => "documents.reviewed",
            "accepted" => "documents.accepted",
            "rejected" => "documents.rejected",
            "request_reupload" => "documents.reupload_requested",
            _ => "documents.reviewed"
        };

        await _db.WriteAuditLogAsync(
            user,
            action,
            "document",
            document.Id,
            document.ClientId,
            JsonSerializer.Serialize(new
            {
                document.Id,
                decision = normalizedDecision,
                reason = reviewDecision.Reason,
                internalNote = reviewDecision.InternalNote,
                document.Status
            }),
            ct);

        await _domainEventDispatcher.DispatchAsync(document.DequeueDomainEvents(), ct);

        if (normalizedDecision is "rejected" or "request_reupload")
        {
            var requestEvents = await EnsureReuploadRequestAsync(document, reviewDecision.Reason ?? "Please re-upload a corrected document.", currentUser, user, ct);
            await _domainEventDispatcher.DispatchAsync(requestEvents, ct);
        }

        return reviewDecision;
    }

    private async Task<IReadOnlyCollection<IDomainEvent>> EnsureReuploadRequestAsync(
        Document document,
        string reason,
        CurrentUserContext currentUser,
        System.Security.Claims.ClaimsPrincipal user,
        CancellationToken ct)
    {
        var existing = await _documents.Requests.FirstOrDefaultAsync(x =>
            x.ClientId == document.ClientId &&
            x.RelatedDocumentId == document.Id &&
            x.RequestType == "reupload_required" &&
            x.Status != "resolved", ct);

        if (existing is not null)
        {
            existing.UpdateDetails(
                existing.RequestType,
                existing.RelatedDocumentId,
                existing.Title,
                reason.Trim(),
                RequestDomainValues.ToRequestPriority(existing.Priority),
                existing.DueDateUtc);
            existing.MarkWaitingOnClient();
            await _documents.SaveChangesAsync(ct);
            return [];
        }

        var requestItem = RequestItem.Create(
            Guid.NewGuid(),
            document.ClientId,
            "reupload_required",
            document.Id,
            $"Re-upload required: {document.Name}",
            reason.Trim(),
            RequestPriority.High,
            currentUser.UserId ?? throw new InvalidOperationException("Authenticated user id is required."),
            RequestStatus.WaitingOnClient,
            null);
        requestItem.RecordCreated(currentUser.ToWorkflowActorContext());

        _documents.Requests.Add(requestItem);
        await _documents.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(
            user,
            "request.created",
            "request",
            requestItem.Id,
            requestItem.ClientId,
            JsonSerializer.Serialize(new { requestItem.RequestType, requestItem.RelatedDocumentId, requestItem.Status }),
            ct);

        return requestItem.DequeueDomainEvents();
    }

    private static string NormalizeDocumentStatus(string rawStatus)
    {
        var normalized = string.IsNullOrWhiteSpace(rawStatus) ? "uploaded" : rawStatus.Trim().ToLowerInvariant();
        return normalized switch
        {
            "draft" => "draft",
            "pending" => "uploaded",
            "submitted" => "uploaded",
            "uploaded" => "uploaded",
            "under_review" => "under_review",
            "accepted" => "accepted",
            "rejected" => "rejected",
            "filed" => "filed",
            _ => "uploaded"
        };
    }
}
