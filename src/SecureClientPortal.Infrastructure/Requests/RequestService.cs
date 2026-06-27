using SecureClientPortal.Backend.Application;
using SecureClientPortal.Backend.Application.Common.Events;
using SecureClientPortal.Backend.Application.Platform;
using SecureClientPortal.Backend.Application.Requests;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Infrastructure.Common.Events;
using SecureClientPortal.Backend.Infrastructure.Notifications.Application;
using SecureClientPortal.Backend.Infrastructure.Requests.Application.Events;

namespace SecureClientPortal.Backend.Infrastructure.Requests.Application;

public sealed class RequestService : IRequestService
{
    private readonly IRequestQueryService _queries;
    private readonly IRequestCommandService _commands;

    public RequestService(IRequestQueryService queries, IRequestCommandService commands)
    {
        _queries = queries;
        _commands = commands;
    }

    internal RequestService(PortalDbContext db)
        : this(
            new RequestQueryService(db, db),
            new RequestCommandService(db, db, new CurrentUserContextFactory(), CreateStandaloneDispatcher(db)))
    {
    }

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

    public Task<(bool forbidden, bool deleted)> DeleteAsync(string id, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default) =>
        _commands.DeleteAsync(id, user, ct);

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
