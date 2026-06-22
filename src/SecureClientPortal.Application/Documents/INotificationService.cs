using SecureClientPortal.Backend.Models;
using System.Security.Claims;

namespace SecureClientPortal.Backend.Application.Documents;

public interface INotificationService
{
    Task<(bool unauthorized, IReadOnlyList<Notification> items)> GetMineAsync(ClaimsPrincipal user, CancellationToken ct = default);
    Task<(bool unauthorized, bool forbidden, Notification? item)> MarkAsReadAsync(string id, ClaimsPrincipal user, CancellationToken ct = default);
    Task<(bool unauthorized, int updated)> MarkAllReadAsync(ClaimsPrincipal user, CancellationToken ct = default);
}
