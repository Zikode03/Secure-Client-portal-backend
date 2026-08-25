using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Application.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;

namespace SecureClientPortal.Backend.Api.Modules.MonthlyPacks;

/// <summary>
/// Client-specific monthly-pack configuration.
/// Clients can add current-month items and request recurring items; accountants/admins control
/// the approved recurring profile used to generate future packs.
/// </summary>
[ApiController]
[Route("api/monthly-pack-profiles")]
[Authorize(Policy = "ClientOrAccountant")]
public sealed class ClientMonthlyPackProfilesController : ControllerBase
{
    private readonly IClientMonthlyPackProfileService _profiles;
    private readonly PortalDbContext _db;

    public ClientMonthlyPackProfilesController(IClientMonthlyPackProfileService profiles, PortalDbContext db)
    {
        _profiles = profiles;
        _db = db;
    }

    [HttpGet("{clientId:guid}")]
    public async Task<IActionResult> Get(Guid clientId, CancellationToken ct)
        => FromResult(await _profiles.GetAsync(clientId, User, ct));

    [HttpPut("{clientId:guid}")]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<IActionResult> Update(
        Guid clientId,
        [FromBody] UpdateClientMonthlyPackProfileRequest request,
        CancellationToken ct)
    {
        // Older UI payloads did not know about recurring due-day metadata. Preserve the currently
        // saved due day when the same logical recurring requirement is sent back without that field.
        var current = await _profiles.GetAsync(clientId, User, ct);
        if (current.Forbidden) return Forbid();

        if (current.Value is not null)
        {
            var enrichedItems = request.RecurringItems.Select(item =>
            {
                if (item.DefaultDueDayOfMonth.HasValue) return item;

                var existing = current.Value.RecurringItems.FirstOrDefault(candidate =>
                    candidate.Source == "client_specific" &&
                    (string.Equals(candidate.Category, item.Category, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(candidate.Label, item.Label, StringComparison.OrdinalIgnoreCase)));

                return item with { DefaultDueDayOfMonth = existing?.DefaultDueDayOfMonth };
            }).ToArray();

            request = request with { RecurringItems = enrichedItems };
        }

        return FromResult(await _profiles.UpdateAsync(clientId, request, User, ct));
    }

    [HttpPost("{clientId:guid}/items")]
    public async Task<IActionResult> AddItem(
        Guid clientId,
        [FromBody] AddClientMonthlyPackItemRequest request,
        CancellationToken ct)
    {
        var result = await _profiles.AddItemAsync(clientId, request, User, ct);

        // A client's request to make an item recurring needs professional attention.
        // Notify both the assigned accountant and admins so the request is not hidden inside the profile screen.
        if (result.Value?.RecurringRequestId is not null && User.IsClient())
        {
            var accountantRecipients = await _db.ResolveNotificationRecipientsAsync(clientId, "accountant", ct);
            var adminRecipients = await _db.ResolveNotificationRecipientsAsync(clientId, "admin", ct);
            await _db.AddNotificationsAsync(
                User,
                accountantRecipients.Concat(adminRecipients),
                clientId,
                "monthly_pack.recurring_requested",
                "Recurring monthly-pack item requested",
                $"The client requested '{request.Label.Trim()}' for every future monthly pack.",
                $"/firm/clients/{clientId}/packs",
                new { result.Value.RecurringRequestId, request.Label, request.Category },
                ct);
        }

        return FromResult(result);
    }

    [HttpPost("{clientId:guid}/recurring/{requestId:guid}/approve")]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<IActionResult> ApproveRecurring(Guid clientId, Guid requestId, CancellationToken ct)
    {
        var result = await _profiles.ApproveRecurringAsync(clientId, requestId, User, ct);
        if (result.Value is not null)
        {
            var clientRecipients = await _db.ResolveNotificationRecipientsAsync(clientId, "client", ct);
            await _db.AddNotificationsAsync(
                User,
                clientRecipients,
                clientId,
                "monthly_pack.recurring_approved",
                "Monthly-pack request approved",
                "Your recurring monthly-pack request was approved and will be included in future monthly packs.",
                "/client/packs",
                new { requestId },
                ct);
        }

        return FromResult(result);
    }

    [HttpPost("{clientId:guid}/recurring/{requestId:guid}/decline")]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<IActionResult> DeclineRecurring(Guid clientId, Guid requestId, CancellationToken ct)
    {
        var result = await _profiles.DeclineRecurringAsync(clientId, requestId, User, ct);
        if (result.Value is not null)
        {
            var clientRecipients = await _db.ResolveNotificationRecipientsAsync(clientId, "client", ct);
            await _db.AddNotificationsAsync(
                User,
                clientRecipients,
                clientId,
                "monthly_pack.recurring_declined",
                "Monthly-pack item kept to this month",
                "Your recurring request was not added to future monthly packs. The item remains available in the current month.",
                "/client/packs",
                new { requestId },
                ct);
        }

        return FromResult(result);
    }

    private IActionResult FromResult<T>(ServiceResult<T> result)
    {
        if (result.Forbidden) return Forbid();
        if (result.NotFound) return string.IsNullOrWhiteSpace(result.Error) ? NotFound() : NotFound(new { error = result.Error });
        if (result.Unauthorized) return StatusCode(result.StatusCode ?? StatusCodes.Status401Unauthorized, new { code = result.ErrorCode, message = result.Error });
        if (!string.IsNullOrWhiteSpace(result.Error)) return StatusCode(result.StatusCode ?? StatusCodes.Status400BadRequest, new { code = result.ErrorCode, error = result.Error });
        return Ok(result.Value);
    }
}
