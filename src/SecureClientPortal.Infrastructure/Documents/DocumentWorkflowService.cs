using Microsoft.AspNetCore.Http;
using SecureClientPortal.Backend.Application;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Common.Events;
using SecureClientPortal.Backend.Application.Documents;
using SecureClientPortal.Backend.Application.Platform;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Infrastructure.Common.Events;
using SecureClientPortal.Backend.Infrastructure.Documents.Application;
using SecureClientPortal.Backend.Infrastructure.Documents.Application.Events;
using SecureClientPortal.Backend.Infrastructure.Notifications.Application;
using SecureClientPortal.Backend.Infrastructure.Requests.Application.Events;
using StorageFileStorage = SecureClientPortal.Backend.Storage.IFileStorage;

namespace SecureClientPortal.Backend.Infrastructure.Documents;

public sealed class DocumentWorkflowService : IDocumentWorkflowService
{
    private readonly IDocumentQueryService _queries;
    private readonly IDocumentCommandService _commands;
    private readonly IDocumentLifecycleService _lifecycle;

    public DocumentWorkflowService(
        IDocumentQueryService queries,
        IDocumentCommandService commands,
        IDocumentLifecycleService lifecycle)
    {
        _queries = queries;
        _commands = commands;
        _lifecycle = lifecycle;
    }

    internal DocumentWorkflowService(PortalDbContext db, StorageFileStorage fileStorage)
        : this(
            new DocumentQueryService(db, db, fileStorage),
            new DocumentCommandService(db, db, fileStorage),
            new DocumentLifecycleService(db, db, new CurrentUserContextFactory(), CreateStandaloneDispatcher(db)))
    {
    }

    public Task<IReadOnlyList<Document>> GetAllAsync(System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default) =>
        _queries.GetAllAsync(user, ct);

    public Task<ServiceResult<IReadOnlyList<Document>>> GetFilingRegisterAsync(System.Security.Claims.ClaimsPrincipal user, string? clientId = null, CancellationToken ct = default) =>
        _queries.GetFilingRegisterAsync(user, clientId, ct);

    public Task<IReadOnlyList<FilingRule>> GetFilingRulesAsync(CancellationToken ct = default) =>
        _queries.GetFilingRulesAsync(ct);

    public Task<ServiceResult<FilingRule>> UpdateFilingRuleAsync(string category, FilingRuleUpdateRequest request, CancellationToken ct = default) =>
        _commands.UpdateFilingRuleAsync(category, request, ct);

    public Task<ServiceResult<object>> UploadAsync(UploadDocumentRequest request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default) =>
        _commands.UploadAsync(request, user, ct);

    public Task<ServiceResult<Document>> CreateAsync(CreateDocumentRequest request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default) =>
        _commands.CreateAsync(request, user, ct);

    public Task<ServiceResult<object>> GetByIdAsync(string id, System.Security.Claims.ClaimsPrincipal user, HttpContext httpContext, CancellationToken ct = default) =>
        _queries.GetByIdAsync(id, user, httpContext, ct);

    public Task<ServiceResult<IReadOnlyList<object>>> GetVersionsAsync(string id, System.Security.Claims.ClaimsPrincipal user, HttpContext httpContext, CancellationToken ct = default) =>
        _queries.GetVersionsAsync(id, user, httpContext, ct);

    public Task<ServiceResult<(StoredFileContent Content, string FileName)>> DownloadAsync(string id, System.Security.Claims.ClaimsPrincipal user, HttpContext httpContext, CancellationToken ct = default) =>
        _queries.DownloadAsync(id, user, httpContext, ct);

    public Task<ServiceResult<Document>> UpdateAsync(string id, UpdateDocumentRequest request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default) =>
        _commands.UpdateAsync(id, request, user, ct);

    public Task<ServiceResult<Document>> UpdateStatusAsync(string id, UpdateDocumentStatusRequest request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default) =>
        _lifecycle.UpdateStatusAsync(id, request, user, ct);

    public Task<ServiceResult<object>> ReviewAsync(string id, AddReviewDecisionRequest request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default) =>
        _lifecycle.ReviewAsync(id, request, user, ct);

    public Task<ServiceResult<object>> RequestReuploadAsync(string id, RequestReuploadRequest request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default) =>
        _lifecycle.RequestReuploadAsync(id, request, user, ct);

    public Task<ServiceResult<DocumentComment>> AddCommentAsync(string id, AddDocumentCommentRequest request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default) =>
        _commands.AddCommentAsync(id, request, user, ct);

    public Task<ServiceResult<IReadOnlyList<DocumentComment>>> GetCommentsAsync(string id, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default) =>
        _queries.GetCommentsAsync(id, user, ct);

    public Task<ServiceResult<bool>> DeleteAsync(string id, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default) =>
        _commands.DeleteAsync(id, user, ct);

    private static IDomainEventDispatcher CreateStandaloneDispatcher(PortalDbContext db)
    {
        var integrationDispatcher = new StandaloneIntegrationEventDispatcher(
        [
            new NotificationRequestedIntegrationEventHandler(db)
        ]);

        return new StandaloneDomainEventDispatcher(
        [
            new DocumentReviewedDomainEventHandler(),
            new RequestCreatedDomainEventHandler(),
            new RequestResolvedDomainEventHandler()
        ], integrationDispatcher);
    }
}
