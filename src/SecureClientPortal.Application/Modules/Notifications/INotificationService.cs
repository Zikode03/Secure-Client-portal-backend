using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts.Modules.Notifications;
using SecureClientPortal.Backend.Models;

namespace SecureClientPortal.Backend.Application.Modules.Notifications;

public interface INotificationService
{
    Task<(bool unauthorized, IReadOnlyList<Notification> items)> GetMineAsync(System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default);
    Task<(bool unauthorized, bool forbidden, Notification? item)> MarkAsReadAsync(string id, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default);
    Task<(bool unauthorized, int updated)> MarkAllReadAsync(System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default);
    Task<ServiceResult<NotificationPreferenceResponse>> GetPreferencesAsync(System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default);
    Task<ServiceResult<NotificationPreferenceResponse>> UpdatePreferencesAsync(UpdateNotificationPreferenceRequest request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default);
}
