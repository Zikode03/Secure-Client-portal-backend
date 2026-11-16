using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SecureClientPortal.Backend.Application.Modules.Banking;

namespace SecureClientPortal.Backend.Api.Modules.Banking;

[ApiController]
[Route("api/banking")]
[Authorize(Policy = "ClientOrAccountant")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType(typeof(BankingErrorResponse), StatusCodes.Status400BadRequest)]
public sealed class BankingController(IBankingService service) : ControllerBase
{
    /// <summary>Register a manual ZAR business account; no bank consent or credentials are requested.</summary>
    [HttpPost("manual/accounts")]
    [ProducesResponseType(typeof(BankingOverviewDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> AddManualAccount([FromBody] ManualBankAccountRequest request, CancellationToken ct) =>
        Result(await service.AddManualAccountAsync(request, User, ct));

    /// <summary>Preview or confirm validated CSV transactions. Preview defaults to true. Import never certifies bank coverage or approves a monthly pack.</summary>
    [HttpPost("manual/import")]
    [ProducesResponseType(typeof(ManualTransactionImportResult), StatusCodes.Status200OK)]
    [RequestSizeLimit(4_000_000)]
    public async Task<IActionResult> ImportManualTransactions([FromBody] ManualTransactionImportRequest request, CancellationToken ct) =>
        Result(await service.ImportManualTransactionsAsync(request, User, ct));

    /// <summary>Read accounts, retained transactions and import history for an authorized business.</summary>
    [HttpGet("overview")]
    [ProducesResponseType(typeof(BankingOverviewDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOverview([FromQuery] Guid? clientId, CancellationToken ct) =>
        Result(await service.GetOverviewAsync(clientId, User, ct));

    /// <summary>Read manual statement readiness or bank coverage when direct integration is enabled.</summary>
    [HttpGet("monthly-pack-status")]
    [ProducesResponseType(typeof(MonthlyPackBankingStatusDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMonthlyPackStatus(
        [FromQuery, BindRequired] Guid clientId,
        [FromQuery, BindRequired] int year,
        [FromQuery, BindRequired] int month,
        CancellationToken ct) =>
        Result(await service.GetMonthlyPackStatusAsync(clientId, year, month, User, ct));

    /// <summary>Development-only sandbox connection; rejected while integration is paused.</summary>
    [HttpPost("sandbox/connect")]
    [ProducesResponseType(typeof(BankingOverviewDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> ConnectSandbox([FromBody] BankingClientRequest request, CancellationToken ct) =>
        Result(await service.ConnectSandboxAsync(request.ClientId, User, ct));

    /// <summary>Sync an authorized connection; rejected while integration is paused.</summary>
    [HttpPost("connections/{connectionId:guid}/sync")]
    [ProducesResponseType(typeof(BankingOverviewDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Sync(Guid connectionId, CancellationToken ct) =>
        Result(await service.SyncAsync(connectionId, User, ct));

    /// <summary>Disconnect an authorized provider; rejected while integration is paused.</summary>
    [HttpPost("connections/{connectionId:guid}/disconnect")]
    [ProducesResponseType(typeof(BankingOverviewDto), StatusCodes.Status200OK)]
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

public sealed record BankingErrorResponse(string Error);
