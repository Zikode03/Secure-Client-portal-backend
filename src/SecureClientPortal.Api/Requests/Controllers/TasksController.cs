using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Models;
using System.IdentityModel.Tokens.Jwt;

namespace SecureClientPortal.Backend.Controllers;

[ApiController]
[Route("api/tasks")]
[Authorize(Policy = "ClientOrAccountant")]
public class TasksController : ControllerBase
{
    private readonly PortalDbContext _db;

    public TasksController(PortalDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<TaskItem>>> GetAll()
    {
        var allowedClientIds = await User.GetAccessibleClientIdsAsync(_db);
        return Ok(await _db.Tasks
            .Where(x => allowedClientIds.Contains(x.ClientId))
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync());
    }

    [HttpPost]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<ActionResult<TaskItem>> Create([FromBody] TaskItem request)
    {
        var allowedClientIds = await User.GetAccessibleClientIdsAsync(_db);
        if (!allowedClientIds.Contains(request.ClientId))
        {
            return Forbid();
        }

        var clientExists = await _db.Clients.AnyAsync(x => x.Id == request.ClientId);
        if (!clientExists)
        {
            return BadRequest(new { error = "Client does not exist." });
        }

        if (string.IsNullOrWhiteSpace(request.Id)) request.Id = $"task_{Guid.NewGuid():N}";
        request.CreatedByUserId = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? request.CreatedByUserId;
        request.CreatedAtUtc = DateTime.UtcNow;
        request.UpdatedAtUtc = request.CreatedAtUtc;

        _db.Tasks.Add(request);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id = request.Id }, request);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<TaskItem>> GetById(string id)
    {
        var allowedClientIds = await User.GetAccessibleClientIdsAsync(_db);
        var item = await _db.Tasks.FindAsync(id);
        if (item is null) return NotFound();
        if (!allowedClientIds.Contains(item.ClientId))
        {
            return Forbid();
        }

        return Ok(item);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<TaskItem>> Update(string id, [FromBody] TaskItem request)
    {
        var allowedClientIds = await User.GetAccessibleClientIdsAsync(_db);
        var item = await _db.Tasks.FindAsync(id);
        if (item is null) return NotFound();
        if (!allowedClientIds.Contains(item.ClientId))
        {
            return Forbid();
        }

        item.Title = request.Title;
        item.Status = request.Status;
        item.Priority = request.Priority;
        item.DueDateUtc = request.DueDateUtc;
        item.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return Ok(item);
    }

    [HttpDelete("{id}")]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<IActionResult> Delete(string id)
    {
        var allowedClientIds = await User.GetAccessibleClientIdsAsync(_db);
        var item = await _db.Tasks.FindAsync(id);
        if (item is null) return NotFound();
        if (!allowedClientIds.Contains(item.ClientId))
        {
            return Forbid();
        }

        _db.Tasks.Remove(item);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
