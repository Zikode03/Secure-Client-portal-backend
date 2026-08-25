using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Application.Modules.MonthlyPacks;

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

    public ClientMonthlyPackProfilesController(IClientMonthlyPackProfileService profiles)
    {
        _profiles = profiles;
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
        => FromResult(await _profiles.UpdateAsync(clientId, request, User, ct));

    [HttpPost("{clientId:guid}/items")]
    public async Task<IActionResult> AddItem(
        Guid clientId,
        [FromBody] AddClientMonthlyPackItemRequest request,
        CancellationToken ct)
        => FromResult(await _profiles.AddItemAsync(clientId, request, User, ct));

    [HttpPost("{clientId:guid}/recurring/{requestId:guid}/approve")]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<IActionResult> ApproveRecurring(Guid clientId, Guid requestId, CancellationToken ct)
        => FromResult(await _profiles.ApproveRecurringAsync(clientId, requestId, User, ct));

    [HttpPost("{clientId:guid}/recurring/{requestId:guid}/decline")]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<IActionResult> DeclineRecurring(Guid clientId, Guid requestId, CancellationToken ct)
        => FromResult(await _profiles.DeclineRecurringAsync(clientId, requestId, User, ct));

    private IActionResult FromResult<T>(ServiceResult<T> result)
    {
        if (result.Forbidden) return Forbid();
        if (result.NotFound) return string.IsNullOrWhiteSpace(result.Error) ? NotFound() : NotFound(new { error = result.Error });
        if (result.Unauthorized) return StatusCode(result.StatusCode ?? StatusCodes.Status401Unauthorized, new { code = result.ErrorCode, message = result.Error });
        if (!string.IsNullOrWhiteSpace(result.Error)) return StatusCode(result.StatusCode ?? StatusCodes.Status400BadRequest, new { code = result.ErrorCode, error = result.Error });
        return Ok(result.Value);
    }
}
