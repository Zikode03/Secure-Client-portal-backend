using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application.Requests;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace SecureClientPortal.Backend.Infrastructure.Requests.Application;

public sealed class TaskService : ITaskService
{
    private readonly PortalDbContext _db;

    public TaskService(PortalDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<TaskItem>> GetAllAsync(ClaimsPrincipal user, CancellationToken ct = default)
    {
        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        return await _db.Tasks
            .Where(x => allowedClientIds.Contains(x.ClientId))
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);
    }

    public async Task<(bool forbidden, TaskItem? item)> GetByIdAsync(string id, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        var item = await _db.Tasks.FindAsync([id], ct);
        if (item is null)
        {
            return (false, null);
        }

        return allowedClientIds.Contains(item.ClientId) ? (false, item) : (true, null);
    }

    public async Task<(bool forbidden, TaskItem created)> CreateAsync(TaskItem request, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(request.ClientId))
        {
            return (true, null!);
        }

        var clientExists = await _db.Clients.AnyAsync(x => x.Id == request.ClientId, ct);
        if (!clientExists)
        {
            throw new ArgumentException("Client does not exist.");
        }

        if (string.IsNullOrWhiteSpace(request.Id))
        {
            request.Id = $"task_{Guid.NewGuid():N}";
        }

        request.CreatedByUserId = user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? request.CreatedByUserId;
        request.CreatedAtUtc = DateTime.UtcNow;
        request.UpdatedAtUtc = request.CreatedAtUtc;

        _db.Tasks.Add(request);
        await _db.SaveChangesAsync(ct);
        return (false, request);
    }

    public async Task<(bool forbidden, TaskItem? updated)> UpdateAsync(string id, TaskItem request, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        var item = await _db.Tasks.FindAsync([id], ct);
        if (item is null)
        {
            return (false, null);
        }

        if (!allowedClientIds.Contains(item.ClientId))
        {
            return (true, null);
        }

        item.Title = request.Title;
        item.Status = request.Status;
        item.Priority = request.Priority;
        item.DueDateUtc = request.DueDateUtc;
        item.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return (false, item);
    }

    public async Task<(bool forbidden, bool deleted)> DeleteAsync(string id, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var allowedClientIds = await user.GetAccessibleClientIdsAsync(_db, ct);
        var item = await _db.Tasks.FindAsync([id], ct);
        if (item is null)
        {
            return (false, false);
        }

        if (!allowedClientIds.Contains(item.ClientId))
        {
            return (true, false);
        }

        _db.Tasks.Remove(item);
        await _db.SaveChangesAsync(ct);
        return (false, true);
    }
}
