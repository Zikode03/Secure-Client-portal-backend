using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts.Modules.Documents;
using SecureClientPortal.Backend.Application.Contracts.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Application.Contracts.Modules.ReviewQueue;
using SecureClientPortal.Backend.Application.Modules.Documents;
using SecureClientPortal.Backend.Application.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Application.Modules.ReviewQueue;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Domain.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Domain.Modules.Documents.Services;
using SecureClientPortal.Backend.Domain.Shared.Modules.Documents;
using System.Security.Claims;
using System.Text.Json;

namespace SecureClientPortal.Backend.Infrastructure.Modules.MonthlyPacks;

public sealed class DocumentSlotService : IDocumentSlotService
{
    private readonly PortalDbContext _db;
    private readonly DocumentSubmissionDomainService _documentSubmissionDomainService;
    private readonly IDocumentWorkflowService _documentWorkflowService;
    private readonly IReviewQueueService _reviewQueueService;

    public DocumentSlotService(
        PortalDbContext db,
        IDocumentWorkflowService documentWorkflowService,
        IReviewQueueService reviewQueueService)
    {
        _db = db;
        _documentSubmissionDomainService = new DocumentSubmissionDomainService();
        _documentWorkflowService = documentWorkflowService;
        _reviewQueueService = reviewQueueService;
    }

    public async Task<(bool forbidden, IReadOnlyList<DocumentSlot>? items)> GetByMonthlyPackIdAsync(string monthlyPackId, ClaimsPrincipal user, CancellationToken ct = default)
    {
        if (!Guid.TryParse(monthlyPackId, out var monthlyPackGuid))
        {
            return (false, null);
        }

        var pack = await _db.MonthlyPacks.FirstOrDefaultAsync(x => x.Id == monthlyPackGuid, ct);
        if (pack is null)
        {
            return (false, null);
        }

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(pack.ClientId))
        {
            return (true, null);
        }

        var slots = await _db.DocumentSlots
            .Where(x => x.MonthlyPackId == monthlyPackGuid)
            .OrderByDescending(x => x.IsRequired)
            .ThenBy(x => x.Label)
            .ToListAsync(ct);

        return (false, slots);
    }

    public async Task<(bool forbidden, DocumentSlot created)> CreateAsync(CreateDocumentSlotRequest request, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var pack = await _db.MonthlyPacks.FirstOrDefaultAsync(x => x.Id == request.MonthlyPackId, ct);
        if (pack is null)
        {
            throw new ArgumentException("Monthly pack was not found.");
        }

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (!user.IsAdmin() && !allowedClientIds.Contains(pack.ClientId))
        {
            return (true, null!);
        }

        var normalizedCategory = DocumentDomainValues.NormalizeCategory(request.Category);
        var existing = await _db.DocumentSlots.FirstOrDefaultAsync(x => x.MonthlyPackId == request.MonthlyPackId && x.Category == normalizedCategory, ct);
        if (existing is not null)
        {
            existing.UpdateDefinition(normalizedCategory, request.Label, request.IsRequired);
            existing.UpdateSchedule(request.DueDateUtc);
            await _db.SaveChangesAsync(ct);
            return (false, existing);
        }

        var slot = DocumentSlot.Create(
            Guid.NewGuid(),
            request.MonthlyPackId,
            pack.ClientId,
            normalizedCategory,
            request.Label,
            request.IsRequired,
            request.DueDateUtc,
            DateTime.UtcNow);
        slot.MarkNotStarted();

        _db.DocumentSlots.Add(slot);
        await _db.SaveChangesAsync(ct);
        return (false, slot);
    }

    public async Task<(bool forbidden, bool invalid, string? error, DocumentSlot? slot)> SubmitAsync(string slotId, ClaimsPrincipal user, CancellationToken ct = default)
    {
        if (!Guid.TryParse(slotId, out var documentSlotId))
        {
            return (false, false, null, null);
        }

        var slot = await _db.DocumentSlots.FirstOrDefaultAsync(x => x.Id == documentSlotId, ct);
        if (slot is null)
        {
            return (false, false, null, null);
        }

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(slot.ClientId))
        {
            return (true, false, null, null);
        }

        var document = !slot.CurrentDocumentId.HasValue
            ? null
            : await _db.Documents.FirstOrDefaultAsync(x => x.Id == slot.CurrentDocumentId.Value && x.ClientId == slot.ClientId, ct);
        var currentVersion = document is null
            ? null
            : await _db.DocumentVersions.FirstOrDefaultAsync(
                x => x.DocumentId == document.Id && x.IsCurrentVersion,
                ct);

        if (document is null || currentVersion is null)
        {
            return (false, true, "The slot must have an uploaded current document version before it can be submitted.", null);
        }

        try
        {
            _documentSubmissionDomainService.Submit(
                document,
                currentVersion,
                slot,
                user.GetUserId() ?? throw new InvalidOperationException("Authenticated user id is required."),
                DateTime.UtcNow);
        }
        catch (DomainRuleException ex)
        {
            return (false, true, ex.Message, null);
        }

        var pack = await _db.MonthlyPacks.FirstOrDefaultAsync(x => x.Id == slot.MonthlyPackId, ct);
        if (pack is not null)
        {
            var slots = await _db.DocumentSlots.Where(x => x.MonthlyPackId == pack.Id).ToListAsync(ct);
            pack.RecalculateStatus(slots);
        }

        await _db.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(
            user,
            "document_slots.submitted",
            "document_slot",
            slot.Id,
            slot.ClientId,
            JsonSerializer.Serialize(new { slot.Id, slot.MonthlyPackId, slot.Status, slot.SubmittedAtUtc, slot.SubmittedByUserId }),
            ct);

        var recipients = await _db.ResolveNotificationRecipientsAsync(slot.ClientId, "accountant", ct);
        await _db.AddNotificationsAsync(
            user,
            recipients,
            slot.ClientId,
            "document_slot.submitted",
            "Document slot submitted",
            $"{slot.Label} was submitted for review.",
            $"/monthly-packs/{slot.ClientId}",
            new { slot.Id, slot.MonthlyPackId, slot.Status, slot.CurrentDocumentId },
            ct);

        return (false, false, null, slot);
    }

    public async Task<(bool forbidden, bool invalid, string? error, DocumentSlot? slot)> MarkNotApplicableAsync(string slotId, ClaimsPrincipal user, CancellationToken ct = default)
    {
        if (!Guid.TryParse(slotId, out var documentSlotId))
        {
            return (false, false, null, null);
        }

        var slot = await _db.DocumentSlots.FirstOrDefaultAsync(x => x.Id == documentSlotId, ct);
        if (slot is null)
        {
            return (false, false, null, null);
        }

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(slot.ClientId))
        {
            return (true, false, null, null);
        }

        if (!user.IsAdmin() && !user.IsAccountant())
        {
            return (true, false, null, null);
        }

        if (slot.Status is "accepted" or "under_review")
        {
            return (false, true, "Accepted or under-review slots cannot be marked not applicable.", null);
        }

        slot.MarkNotApplicable();

        var pack = await _db.MonthlyPacks.FirstOrDefaultAsync(x => x.Id == slot.MonthlyPackId, ct);
        if (pack is not null)
        {
            var slots = await _db.DocumentSlots.Where(x => x.MonthlyPackId == pack.Id).ToListAsync(ct);
            pack.RecalculateStatus(slots);
        }

        await _db.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(
            user,
            "document_slots.marked_not_applicable",
            "document_slot",
            slot.Id,
            slot.ClientId,
            JsonSerializer.Serialize(new { slot.Id, slot.MonthlyPackId, slot.Status }),
            ct);

        return (false, false, null, slot);
    }

    public async Task<ServiceResult<DocumentSlot>> UploadAsync(string slotId, UploadDocumentSlotRequest request, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var slot = await ResolveAccessibleSlotAsync(slotId, user, ct);
        if (slot.Forbidden) return ServiceResult<DocumentSlot>.ForbiddenResult();
        if (slot.NotFound || slot.Value is null) return ServiceResult<DocumentSlot>.NotFoundResult();

        var packStatus = await _db.MonthlyPacks
            .Where(x => x.Id == slot.Value.MonthlyPackId)
            .Select(x => x.Status)
            .FirstOrDefaultAsync(ct);
        if (packStatus is "under_review" or "complete" or "closed")
        {
            return ServiceResult<DocumentSlot>.ErrorResult(
                "This monthly pack has already been submitted and cannot be changed unless the accountant requests a re-upload.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var uploadResult = await _documentWorkflowService.UploadAsync(new UploadDocumentRequest
        {
            ClientId = slot.Value.ClientId,
            MonthlyPackId = slot.Value.MonthlyPackId,
            DocumentSlotId = slot.Value.Id,
            DocumentType = slot.Value.Category,
            DocumentId = slot.Value.CurrentDocumentId,
            File = request.File
        }, user, ct);

        if (uploadResult.Forbidden) return ServiceResult<DocumentSlot>.ForbiddenResult();
        if (uploadResult.NotFound) return ServiceResult<DocumentSlot>.NotFoundResult(uploadResult.Error);
        if (!string.IsNullOrWhiteSpace(uploadResult.Error))
        {
            return ServiceResult<DocumentSlot>.ErrorResult(uploadResult.Error, uploadResult.ErrorCode, uploadResult.StatusCode ?? 400);
        }

        var refreshed = await _db.DocumentSlots.FirstOrDefaultAsync(x => x.Id == slot.Value.Id, ct);
        return refreshed is null
            ? ServiceResult<DocumentSlot>.NotFoundResult()
            : ServiceResult<DocumentSlot>.Success(refreshed);
    }

    public async Task<ServiceResult<IReadOnlyList<object>>> GetVersionsAsync(string slotId, ClaimsPrincipal user, HttpContext httpContext, CancellationToken ct = default)
    {
        var slot = await ResolveAccessibleSlotAsync(slotId, user, ct);
        if (slot.Forbidden) return ServiceResult<IReadOnlyList<object>>.ForbiddenResult();
        if (slot.NotFound || slot.Value is null) return ServiceResult<IReadOnlyList<object>>.NotFoundResult();
        if (!slot.Value.CurrentDocumentId.HasValue)
        {
            return ServiceResult<IReadOnlyList<object>>.Success([]);
        }

        return await _documentWorkflowService.GetVersionsAsync(slot.Value.CurrentDocumentId.Value.ToString(), user, httpContext, ct);
    }

    public async Task<ServiceResult<DocumentSlotWorkspaceResponse>> GetWorkspaceAsync(string slotId, ClaimsPrincipal user, HttpContext httpContext, CancellationToken ct = default)
    {
        var slot = await ResolveAccessibleSlotAsync(slotId, user, ct);
        if (slot.Forbidden) return ServiceResult<DocumentSlotWorkspaceResponse>.ForbiddenResult();
        if (slot.NotFound || slot.Value is null) return ServiceResult<DocumentSlotWorkspaceResponse>.NotFoundResult();
        if (!slot.Value.CurrentDocumentId.HasValue)
        {
            return ServiceResult<DocumentSlotWorkspaceResponse>.ErrorResult("The slot does not have an uploaded document yet.");
        }

        var workspace = await _reviewQueueService.GetWorkspaceAsync(slot.Value.CurrentDocumentId.Value.ToString(), user, httpContext, ct);
        if (workspace.Forbidden) return ServiceResult<DocumentSlotWorkspaceResponse>.ForbiddenResult();
        if (workspace.NotFound) return ServiceResult<DocumentSlotWorkspaceResponse>.NotFoundResult(workspace.Error);
        if (!string.IsNullOrWhiteSpace(workspace.Error))
        {
            return ServiceResult<DocumentSlotWorkspaceResponse>.ErrorResult(workspace.Error, workspace.ErrorCode, workspace.StatusCode ?? 400);
        }

        var refreshed = await _db.DocumentSlots.FirstAsync(x => x.Id == slot.Value.Id, ct);
        return ServiceResult<DocumentSlotWorkspaceResponse>.Success(new DocumentSlotWorkspaceResponse(
            Map(refreshed),
            workspace.Value!));
    }

    public Task<ServiceResult<object>> StartReviewAsync(string slotId, StartDocumentSlotReviewRequest request, ClaimsPrincipal user, CancellationToken ct = default) =>
        ReviewSlotAsync(slotId, new AddReviewDecisionRequest("under_review", null, request.InternalNote), user, ct);

    public Task<ServiceResult<object>> ApproveAsync(string slotId, ApproveDocumentSlotRequest request, ClaimsPrincipal user, CancellationToken ct = default) =>
        ReviewSlotAsync(slotId, new AddReviewDecisionRequest("accepted", null, request.InternalNote), user, ct);

    public Task<ServiceResult<object>> RejectAsync(string slotId, RejectDocumentSlotRequest request, ClaimsPrincipal user, CancellationToken ct = default) =>
        ReviewSlotAsync(slotId, new AddReviewDecisionRequest("rejected", request.Reason, request.InternalNote), user, ct);

    public async Task<ServiceResult<object>> RequestReuploadAsync(string slotId, RequestDocumentSlotReuploadRequest request, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var slot = await ResolveAccessibleSlotAsync(slotId, user, ct);
        if (slot.Forbidden) return ServiceResult<object>.ForbiddenResult();
        if (slot.NotFound || slot.Value is null) return ServiceResult<object>.NotFoundResult();
        if (!slot.Value.CurrentDocumentId.HasValue)
        {
            return ServiceResult<object>.ErrorResult("The slot does not have an uploaded document yet.");
        }

        return await _documentWorkflowService.RequestReuploadAsync(
            slot.Value.CurrentDocumentId.Value.ToString(),
            new RequestReuploadRequest(request.Reason, request.InternalNote),
            user,
            ct);
    }

    private async Task<ServiceResult<object>> ReviewSlotAsync(string slotId, AddReviewDecisionRequest request, ClaimsPrincipal user, CancellationToken ct)
    {
        var slot = await ResolveAccessibleSlotAsync(slotId, user, ct);
        if (slot.Forbidden) return ServiceResult<object>.ForbiddenResult();
        if (slot.NotFound || slot.Value is null) return ServiceResult<object>.NotFoundResult();
        if (!slot.Value.CurrentDocumentId.HasValue)
        {
            return ServiceResult<object>.ErrorResult("The slot does not have an uploaded document yet.");
        }

        return await _documentWorkflowService.ReviewAsync(slot.Value.CurrentDocumentId.Value.ToString(), request, user, ct);
    }

    private async Task<ServiceResult<DocumentSlot>> ResolveAccessibleSlotAsync(string slotId, ClaimsPrincipal user, CancellationToken ct)
    {
        if (!Guid.TryParse(slotId, out var documentSlotId))
        {
            return ServiceResult<DocumentSlot>.NotFoundResult();
        }

        var slot = await _db.DocumentSlots.FirstOrDefaultAsync(x => x.Id == documentSlotId, ct);
        if (slot is null)
        {
            return ServiceResult<DocumentSlot>.NotFoundResult();
        }

        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        return allowedClientIds.Contains(slot.ClientId)
            ? ServiceResult<DocumentSlot>.Success(slot)
            : ServiceResult<DocumentSlot>.ForbiddenResult();
    }

    private static DocumentSlotResponse Map(DocumentSlot slot) =>
        new(
            slot.Id,
            slot.MonthlyPackId,
            slot.ClientId,
            slot.Category,
            slot.Label,
            slot.IsRequired,
            slot.Status,
            slot.CanCurrentlyBeSubmitted,
            slot.CurrentDocumentId,
            slot.DueDateUtc,
            slot.SubmittedAtUtc,
            slot.SubmittedByUserId,
            slot.ReviewStatus,
            slot.RejectionReason,
            slot.CreatedAtUtc,
            slot.UpdatedAtUtc);
}


