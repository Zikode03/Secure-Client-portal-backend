using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureClientPortal.Backend.Application.Contracts.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Application.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Domain.Modules.MonthlyPacks;

namespace SecureClientPortal.Backend.Api.Modules.MonthlyPacks;

[ApiController]
[Route("api/monthly-packs")]
[Authorize(Policy = "ClientOrAccountant")]
public class MonthlyPacksController : ControllerBase
{
    private readonly IMonthlyPackService _monthlyPackService;

    public MonthlyPacksController(IMonthlyPackService monthlyPackService)
    {
        _monthlyPackService = monthlyPackService;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<MonthlyPackResponse>>> GetAll([FromQuery] string? clientId = null, CancellationToken ct = default)
    {
        var result = await _monthlyPackService.GetAllAsync(User, clientId, ct);
        if (result.forbidden)
        {
            return Forbid();
        }

        return Ok(result.items.Select(Map));
    }

    [HttpGet("{clientId}/{year:int}/{month:int}")]
    public async Task<ActionResult<MonthlyPackResponse>> GetByClientAndPeriod(string clientId, int year, int month, CancellationToken ct)
    {
        var result = await _monthlyPackService.GetByClientAndPeriodAsync(clientId, year, month, User, ct);
        if (result.forbidden)
        {
            return Forbid();
        }

        if (result.pack is null)
        {
            return NotFound();
        }

        return Ok(Map(result.pack));
    }

    [HttpPost]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<ActionResult<MonthlyPackResponse>> Create([FromBody] CreateMonthlyPackRequest request, CancellationToken ct)
    {
        var result = await _monthlyPackService.CreateAsync(request, User, ct);
        if (result.forbidden)
        {
            return Forbid();
        }

        return Created($"/api/monthly-packs/{result.created.ClientId}/{result.created.Year}/{result.created.Month}", Map(result.created));
    }

    [HttpPost("{id}/submit")]
    public async Task<ActionResult<MonthlyPackResponse>> Submit(string id, CancellationToken ct)
    {
        var result = await _monthlyPackService.SubmitAsync(id, User, ct);
        if (result.forbidden)
        {
            return Forbid();
        }

        if (result.invalid)
        {
            return BadRequest(new { error = result.error });
        }

        if (result.pack is null)
        {
            return NotFound();
        }

        return Ok(Map(result.pack));
    }

    [HttpPost("{id}/close")]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<ActionResult<MonthlyPackResponse>> Close(string id, CancellationToken ct)
    {
        var result = await _monthlyPackService.CloseAsync(id, User, ct);
        if (result.forbidden)
        {
            return Forbid();
        }

        if (result.invalid)
        {
            return BadRequest(new { error = result.error });
        }

        if (result.pack is null)
        {
            return NotFound();
        }

        return Ok(Map(result.pack));
    }

    private static MonthlyPackResponse Map(MonthlyPack pack) =>
        new(
            pack.Id,
            pack.ClientId,
            pack.Year,
            pack.Month,
            pack.Status,
            pack.CreatedAtUtc,
            pack.UpdatedAtUtc);
}
