using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureClientPortal.Backend.Application.Modules.Banking;

namespace SecureClientPortal.Backend.Api.Modules.Banking;

[ApiController]
[Route("api/banking")]
[Authorize(Policy = "ClientOrAccountant")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class BankingController(IBankingService service) : ControllerBase
{
    [HttpGet("overview")]
    public async Task<IActionResult> GetOverview([FromQuery] Guid? clientId, CancellationToken ct) =>
        Result(await service.GetOverviewAsync(clientId, User, ct));

    [HttpGet("monthly-pack-status")]
    public async Task<IActionResult> GetMonthlyPackStatus(
        [FromQuery] Guid clientId,
        [FromQuery] int year,
        [FromQuery] int month,
        CancellationToken ct) =>
        Result(await service.GetMonthlyPackStatusAsync(clientId, year, month, User, ct));

    [HttpPost("sandbox/connect")]
    public async Task<IActionResult> ConnectSandbox([FromBody] BankingClientRequest request, CancellationToken ct) =>
        Result(await service.ConnectSandboxAsync(request.ClientId, User, ct));

    [HttpPost("connections/{connectionId:guid}/sync")]
    public async Task<IActionResult> Sync(Guid connectionId, CancellationToken ct) =>
        Result(await service.SyncAsync(connectionId, User, ct));

    [HttpPost("connections/{connectionId:guid}/disconnect")]
    public async Task<IActionResult> Disconnect(Guid connectionId, CancellationToken ct) =>
        Result(await service.DisconnectAsync(connectionId, User, ct));

    private IActionResult Result<T>(BankingOperationResult<T> result)
    {
        if (result.Forbidden) return Forbid();
        if (!result.Success || result.Value is null) return BadRequest(new { error = result.Error ?? "Banking request failed." });
        return Ok(result.Value);
    }
}

public sealed record BankingClientRequest(Guid? ClientId);
