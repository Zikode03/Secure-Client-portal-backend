using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureClientPortal.Backend.Application.Contracts.Modules.Clients;
using SecureClientPortal.Backend.Application.Modules.Clients;
using SecureClientPortal.Backend.Models;

namespace SecureClientPortal.Backend.Api.Modules.Clients;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "ClientOrAccountant")]
public class ClientsController : ControllerBase
{
    private readonly IClientService _clientService;

    public ClientsController(IClientService clientService)
    {
        _clientService = clientService;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Client>>> GetAll(CancellationToken ct)
    {
        var clients = await _clientService.GetAllAsync(User, ct);
        return Ok(clients);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Client>> GetById(string id, CancellationToken ct)
    {
        var result = await _clientService.GetByIdAsync(id, User, ct);
        if (result.forbidden)
        {
            return Forbid();
        }

        if (result.client is null)
        {
            return NotFound();
        }

        return Ok(result.client);
    }

    [HttpPost]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<ActionResult<Client>> Create([FromBody] Client request, CancellationToken ct)
    {
        try
        {
            var result = await _clientService.CreateAsync(request, User, ct);
            if (result.forbidden)
            {
                return Forbid();
            }

            return CreatedAtAction(nameof(GetById), new { id = result.created.Id }, result.created);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("{id}")]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<ActionResult<Client>> Update(string id, [FromBody] Client request, CancellationToken ct)
    {
        var result = await _clientService.UpdateAsync(id, request, User, ct);
        if (result.forbidden)
        {
            return Forbid();
        }

        if (result.updated is null)
        {
            return NotFound();
        }

        return Ok(result.updated);
    }

    [HttpPut("{id}/status")]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<ActionResult<Client>> UpdateStatus(string id, [FromBody] UpdateClientStatusRequest request, CancellationToken ct)
    {
        var result = await _clientService.UpdateStatusAsync(id, request, User, ct);
        if (result.forbidden)
        {
            return Forbid();
        }

        if (result.updated is null)
        {
            return NotFound();
        }

        return Ok(result.updated);
    }

    [HttpGet("{id}/profile")]
    public async Task<IActionResult> GetBusinessProfile(string id, CancellationToken ct)
    {
        return FromResult(await _clientService.GetBusinessProfileAsync(id, User, ct));
    }

    [HttpPut("{id}/profile")]
    public async Task<IActionResult> UpdateBusinessProfile(string id, [FromBody] UpdateClientBusinessProfileRequest request, CancellationToken ct)
    {
        return FromResult(await _clientService.UpdateBusinessProfileAsync(id, request, User, ct));
    }

    [HttpDelete("{id}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var deleted = await _clientService.DeleteAsync(id, ct);
        if (!deleted)
        {
            return NotFound();
        }

        return NoContent();
    }

    private IActionResult FromResult<T>(SecureClientPortal.Backend.Application.Common.ServiceResult<T> result)
    {
        if (result.Forbidden) return Forbid();
        if (result.NotFound) return NotFound();
        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            return StatusCode(result.StatusCode ?? StatusCodes.Status400BadRequest, new { code = result.ErrorCode, error = result.Error });
        }

        return Ok(result.Value);
    }
}
