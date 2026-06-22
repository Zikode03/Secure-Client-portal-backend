using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureClientPortal.Backend.Application.Documents;
using SecureClientPortal.Backend.Models;

namespace SecureClientPortal.Backend.Controllers;

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
}
