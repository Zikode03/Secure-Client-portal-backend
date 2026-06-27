using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application.Requests;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;

namespace SecureClientPortal.Backend.Infrastructure.Requests.Application;

public sealed class RequestQueryService : IRequestQueryService
{
    private readonly IRequestModuleDbContext _requests;
    private readonly PortalDbContext _db;

    public RequestQueryService(IRequestModuleDbContext requests, PortalDbContext db)
    {
        _requests = requests;
        _db = db;
    }

    public async Task<(bool forbidden, IReadOnlyList<RequestItem> results)> GetAllAsync(System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default)
    {
        await RefreshOverdueRequestsAsync(ct);
        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        var items = await _requests.Requests
            .Where(x => allowedClientIds.Contains(x.ClientId))
            .OrderByDescending(x => x.RequestedAtUtc)
            .ToListAsync(ct);
        return (false, items);
    }

    public async Task<(bool forbidden, RequestItem? item)> GetByIdAsync(string id, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default)
    {
        await RefreshOverdueRequestsAsync(ct);
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
        return allowedClientIds.Contains(item.ClientId) ? (false, item) : (true, null);
    }

    public async Task<(bool forbidden, IReadOnlyList<RequestComment>? comments)> GetCommentsAsync(string id, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default)
    {
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

        var comments = await _requests.RequestComments
            .Where(x => x.RequestId == item.Id)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(ct);

        return (false, comments);
    }

    private async Task RefreshOverdueRequestsAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var overdueRequests = await _requests.Requests
            .Where(x =>
                x.Status != RequestStatus.Resolved.ToStorageValue() &&
                x.DueDateUtc != null &&
                x.DueDateUtc < now &&
                x.Status != RequestStatus.Overdue.ToStorageValue())
            .ToListAsync(ct);

        if (overdueRequests.Count == 0)
        {
            return;
        }

        RequestWorkflowPolicy.RefreshOverdue(overdueRequests, now);
        await _requests.SaveChangesAsync(ct);
    }
}
