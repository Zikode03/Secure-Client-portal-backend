using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts.Modules.Notifications;
using SecureClientPortal.Backend.Application.Modules.Notifications;
using SecureClientPortal.Backend.Models;

namespace SecureClientPortal.Backend.Api.Modules.Notifications;

[ApiController]
[Route("api/notifications")]
[Authorize(Policy = "ClientOrAccountant")]
public class NotificationsController : ControllerBase
{
    private readonly INotificationService _notificationService;

    public NotificationsController(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Notification>>> GetMine(CancellationToken ct)
    {
        var result = await _notificationService.GetMineAsync(User, ct);
        if (result.unauthorized)
        {
            return Unauthorized();
        }

        return Ok(result.items);
    }

    [HttpPost("{id}/mark-read")]
    public async Task<ActionResult<Notification>> MarkAsRead(string id, CancellationToken ct)
    {
        var result = await _notificationService.MarkAsReadAsync(id, User, ct);
        if (result.unauthorized)
        {
            return Unauthorized();
        }

        if (result.forbidden)
        {
            return Forbid();
        }

        if (result.item is null)
        {
            return NotFound();
        }

        return Ok(result.item);
    }

    [HttpPost("mark-all-read")]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        var result = await _notificationService.MarkAllReadAsync(User, ct);
        if (result.unauthorized)
        {
            return Unauthorized();
        }

        return Ok(new { updated = result.updated });
    }

    [HttpGet("preferences")]
    public async Task<IActionResult> GetPreferences(CancellationToken ct)
    {
        return FromResult(await _notificationService.GetPreferencesAsync(User, ct));
    }

    [HttpPut("preferences")]
    public async Task<IActionResult> UpdatePreferences([FromBody] UpdateNotificationPreferenceRequest request, CancellationToken ct)
    {
        return FromResult(await _notificationService.UpdatePreferencesAsync(request, User, ct));
    }

    private IActionResult FromResult<T>(ServiceResult<T> result)
    {
        if (result.Unauthorized) return Unauthorized();
        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            return StatusCode(result.StatusCode ?? StatusCodes.Status400BadRequest, new { code = result.ErrorCode, error = result.Error });
        }

        return Ok(result.Value);
    }
}
