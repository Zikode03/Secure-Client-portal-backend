using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureClientPortal.Backend.Application.Contracts;
using SecureClientPortal.Backend.Application.Documents;
using SecureClientPortal.Backend.Models;

namespace SecureClientPortal.Backend.Controllers;

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
    public async Task<ActionResult<IEnumerable<MonthlyPack>>> GetAll([FromQuery] string? clientId = null, CancellationToken ct = default)
    {
        var result = await _monthlyPackService.GetAllAsync(User, clientId, ct);
        if (result.forbidden)
        {
            return Forbid();
        }

        return Ok(result.items);
    }

    [HttpGet("{clientId}/{year:int}/{month:int}")]
    public async Task<ActionResult<MonthlyPack>> GetByClientAndPeriod(string clientId, int year, int month, CancellationToken ct)
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

        return Ok(result.pack);
    }

    [HttpPost]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<ActionResult<MonthlyPack>> Create([FromBody] CreateMonthlyPackRequest request, CancellationToken ct)
    {
        var result = await _monthlyPackService.CreateAsync(request, User, ct);
        if (result.forbidden)
        {
            return Forbid();
        }

        return Created($"/api/monthly-packs/{result.created.ClientId}/{result.created.Year}/{result.created.Month}", result.created);
    }

    [HttpPost("{id}/submit")]
    public async Task<ActionResult<MonthlyPack>> Submit(string id, CancellationToken ct)
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

        return Ok(result.pack);
    }
}
