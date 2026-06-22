using SecureClientPortal.Backend.Models;
using System.Security.Claims;

namespace SecureClientPortal.Backend.Application.Requests;

public interface ITaskService
{
    Task<IReadOnlyList<TaskItem>> GetAllAsync(ClaimsPrincipal user, CancellationToken ct = default);
    Task<(bool forbidden, TaskItem? item)> GetByIdAsync(string id, ClaimsPrincipal user, CancellationToken ct = default);
    Task<(bool forbidden, TaskItem created)> CreateAsync(TaskItem request, ClaimsPrincipal user, CancellationToken ct = default);
    Task<(bool forbidden, TaskItem? updated)> UpdateAsync(string id, TaskItem request, ClaimsPrincipal user, CancellationToken ct = default);
    Task<(bool forbidden, bool deleted)> DeleteAsync(string id, ClaimsPrincipal user, CancellationToken ct = default);
}
