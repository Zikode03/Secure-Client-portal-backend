using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureClientPortal.Backend.Application.Requests;
using SecureClientPortal.Backend.Models;

namespace SecureClientPortal.Backend.Controllers;

[ApiController]
[Route("api/tasks")]
[Authorize(Policy = "ClientOrAccountant")]
public class TasksController : ControllerBase
{
    private readonly ITaskService _tasks;

    public TasksController(ITaskService tasks)
    {
        _tasks = tasks;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<TaskItem>>> GetAll(CancellationToken ct)
    {
        return Ok(await _tasks.GetAllAsync(User, ct));
    }

    [HttpPost]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<ActionResult<TaskItem>> Create([FromBody] TaskItem request, CancellationToken ct)
    {
        try
        {
            var result = await _tasks.CreateAsync(request, User, ct);
            return result.forbidden ? Forbid() : CreatedAtAction(nameof(GetById), new { id = result.created.Id }, result.created);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<TaskItem>> GetById(string id, CancellationToken ct)
    {
        var result = await _tasks.GetByIdAsync(id, User, ct);
        if (result.forbidden)
        {
            return Forbid();
        }

        return result.item is null ? NotFound() : Ok(result.item);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<TaskItem>> Update(string id, [FromBody] TaskItem request, CancellationToken ct)
    {
        var result = await _tasks.UpdateAsync(id, request, User, ct);
        if (result.forbidden)
        {
            return Forbid();
        }

        return result.updated is null ? NotFound() : Ok(result.updated);
    }

    [HttpDelete("{id}")]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var result = await _tasks.DeleteAsync(id, User, ct);
        if (result.forbidden)
        {
            return Forbid();
        }

        return result.deleted ? NoContent() : NotFound();
    }
}
