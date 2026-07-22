using SecureClientPortal.Backend.Application;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Common.Events;
using SecureClientPortal.Backend.Application.Contracts.Modules.Documents;
using SecureClientPortal.Backend.Application.Contracts.Modules.Requests;
using SecureClientPortal.Backend.Application.Modules.AuditLogs;
using SecureClientPortal.Backend.Application.Modules.Documents;
using SecureClientPortal.Backend.Application.Modules.Requests;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Infrastructure.Common.Events;
using SecureClientPortal.Backend.Infrastructure.Modules.Notifications.Application;
using SecureClientPortal.Backend.Infrastructure.Modules.Requests.Application;
using SecureClientPortal.Backend.Infrastructure.Modules.Requests.Application.Events;

namespace SecureClientPortal.Backend.Infrastructure.Modules.Requests;

public sealed class RequestService : IRequestService
{
    private readonly IRequestQueryService _queries;
    private readonly IRequestCommandService _commands;
    private readonly IDocumentWorkflowService? _documentWorkflowService;

    public RequestService(IRequestQueryService queries, IRequestCommandService commands, IDocumentWorkflowService? documentWorkflowService = null)
    {
        _queries = queries;
        _commands = commands;
        _documentWorkflowService = documentWorkflowService;
    }

    public static RequestService CreateForTests(PortalDbContext db, IDocumentWorkflowService? documentWorkflowService = null) =>
        new(
            new RequestQueryService(db, db),
            new RequestCommandService(db, db, new CurrentUserContextFactory(), CreateStandaloneDispatcher(db)),
            documentWorkflowService);

    public Task<(bool forbidden, IReadOnlyList<RequestItem> results)> GetAllAsync(System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default) =>
        _queries.GetAllAsync(user, ct);

    public Task<(bool forbidden, RequestItem? item)> GetByIdAsync(string id, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default) =>
        _queries.GetByIdAsync(id, user, ct);

    public Task<(bool forbidden, RequestItem created)> CreateAsync(CreateRequestRequest request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default) =>
        _commands.CreateAsync(request, user, ct);

    public Task<(bool forbidden, RequestItem? updated)> UpdateAsync(string id, UpdateRequestRequest request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default) =>
        _commands.UpdateAsync(id, request, user, ct);

    public Task<(bool forbidden, RequestItem? updated)> UpdateStatusAsync(string id, UpdateRequestStatusRequest request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default) =>
        _commands.UpdateStatusAsync(id, request, user, ct);

    public Task<(bool forbidden, IReadOnlyList<RequestComment>? comments)> GetCommentsAsync(string id, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default) =>
        _queries.GetCommentsAsync(id, user, ct);

    public Task<(bool forbidden, RequestComment? comment)> AddCommentAsync(string id, AddRequestCommentRequest request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default) =>
        _commands.AddCommentAsync(id, request, user, ct);

    public Task<(bool forbidden, RequestItem? resolved)> ResolveAsync(string id, ResolveRequestRequest request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default) =>
        _commands.ResolveAsync(id, request, user, ct);

    public Task<ServiceResult<RequestItem>> EscalateAsync(string id, EscalateRequestRequest request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default) =>
        _commands.EscalateAsync(id, request, user, ct);

    public Task<(bool forbidden, bool deleted)> DeleteAsync(string id, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default) =>
        _commands.DeleteAsync(id, user, ct);

    public Task<ServiceResult<RequestWorkspaceResponse>> GetWorkspaceAsync(string id, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default) =>
        _queries.GetWorkspaceAsync(id, user, ct);

    public async Task<ServiceResult<RequestDocumentUploadResponse>> UploadDocumentAsync(string id, UploadRequestDocumentRequest request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default)
    {
        RequestValidators.ValidateUpload(request);

        if (_documentWorkflowService is null)
        {
            throw new InvalidOperationException("Document workflow service is required for request uploads.");
        }

        var workspaceResult = await _queries.GetWorkspaceAsync(id, user, ct);
        if (workspaceResult.Forbidden)
        {
            return ServiceResult<RequestDocumentUploadResponse>.ForbiddenResult(workspaceResult.Error, workspaceResult.ErrorCode);
        }

        if (workspaceResult.NotFound || workspaceResult.Value is null)
        {
            return ServiceResult<RequestDocumentUploadResponse>.NotFoundResult(workspaceResult.Error, workspaceResult.ErrorCode);
        }

        if (workspaceResult.Value.RelatedDocument is null || !workspaceResult.Value.CanUploadCorrection)
        {
            return ServiceResult<RequestDocumentUploadResponse>.ErrorResult("This request is not ready for a correction upload.");
        }

        var relatedDocument = workspaceResult.Value.RelatedDocument;
        var uploadResult = await _documentWorkflowService.UploadAsync(new UploadDocumentRequest
        {
            ClientId = relatedDocument.ClientId,
            MonthlyPackId = relatedDocument.MonthlyPackId,
            DocumentSlotId = relatedDocument.DocumentSlotId,
            DocumentType = relatedDocument.Category,
            DocumentId = relatedDocument.Id,
            File = request.File
        }, user, ct);

        if (uploadResult.Forbidden)
        {
            return ServiceResult<RequestDocumentUploadResponse>.ForbiddenResult(uploadResult.Error, uploadResult.ErrorCode);
        }

        if (uploadResult.NotFound)
        {
            return ServiceResult<RequestDocumentUploadResponse>.NotFoundResult(uploadResult.Error, uploadResult.ErrorCode);
        }

        if (uploadResult.Unauthorized)
        {
            return ServiceResult<RequestDocumentUploadResponse>.UnauthorizedResult(uploadResult.Error, uploadResult.ErrorCode, uploadResult.StatusCode ?? 401);
        }

        if (!string.IsNullOrWhiteSpace(uploadResult.Error))
        {
            return ServiceResult<RequestDocumentUploadResponse>.ErrorResult(uploadResult.Error, uploadResult.ErrorCode, uploadResult.StatusCode ?? 400);
        }

        var requestUpdateResult = await _commands.MarkDocumentUploadedAsync(id, request.Message, user, ct);
        if (requestUpdateResult.Forbidden)
        {
            return ServiceResult<RequestDocumentUploadResponse>.ForbiddenResult(requestUpdateResult.Error, requestUpdateResult.ErrorCode);
        }

        if (requestUpdateResult.NotFound)
        {
            return ServiceResult<RequestDocumentUploadResponse>.NotFoundResult(requestUpdateResult.Error, requestUpdateResult.ErrorCode);
        }

        if (!string.IsNullOrWhiteSpace(requestUpdateResult.Error))
        {
            return ServiceResult<RequestDocumentUploadResponse>.ErrorResult(requestUpdateResult.Error, requestUpdateResult.ErrorCode, requestUpdateResult.StatusCode ?? 400);
        }

        var refreshedWorkspaceResult = await _queries.GetWorkspaceAsync(id, user, ct);
        if (refreshedWorkspaceResult.Forbidden)
        {
            return ServiceResult<RequestDocumentUploadResponse>.ForbiddenResult(refreshedWorkspaceResult.Error, refreshedWorkspaceResult.ErrorCode);
        }

        if (refreshedWorkspaceResult.NotFound || refreshedWorkspaceResult.Value is null)
        {
            return ServiceResult<RequestDocumentUploadResponse>.NotFoundResult(refreshedWorkspaceResult.Error, refreshedWorkspaceResult.ErrorCode);
        }

        return ServiceResult<RequestDocumentUploadResponse>.Success(new RequestDocumentUploadResponse(
            refreshedWorkspaceResult.Value,
            "Corrected document uploaded and returned to accountant review."));
    }

    private static IDomainEventDispatcher CreateStandaloneDispatcher(PortalDbContext db)
    {
        var integrationDispatcher = new StandaloneIntegrationEventDispatcher(
        [
            new NotificationRequestedIntegrationEventHandler(db)
        ]);

        return new StandaloneDomainEventDispatcher(
        [
            new RequestCreatedDomainEventHandler(),
            new RequestResolvedDomainEventHandler()
        ], integrationDispatcher);
    }
}
