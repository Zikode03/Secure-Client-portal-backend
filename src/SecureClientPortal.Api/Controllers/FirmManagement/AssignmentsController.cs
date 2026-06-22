using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureClientPortal.Backend.Application;
using SecureClientPortal.Backend.Application.Contracts;
using SecureClientPortal.Backend.Application.Assignments;

namespace SecureClientPortal.Backend.Controllers;

[ApiController]
[Route("api/assignments")]
[Authorize(Policy = "ClientOrAccountant")]
public class AssignmentsController : ControllerBase
{
    private readonly IAssignmentService _assignments;
    private readonly ICurrentUserContextFactory _currentUserContextFactory;

    public AssignmentsController(IAssignmentService assignments, ICurrentUserContextFactory currentUserContextFactory)
    {
        _assignments = assignments;
        _currentUserContextFactory = currentUserContextFactory;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<object>>> GetAll([FromQuery] string? clientId = null)
    {
        var result = await _assignments.GetAllAsync(User, clientId);
        return result.forbidden ? Forbid() : Ok(result.results);
    }

    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Create([FromBody] CreateAssignmentRequest request)
    {
        try
        {
            var created = await _assignments.CreateAsync(request, _currentUserContextFactory.Create(User, HttpContext));
            return Created("/api/assignments", created);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("{id}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Delete(string id)
    {
        try
        {
            var deleted = await _assignments.DeleteAsync(id, _currentUserContextFactory.Create(User, HttpContext));
            return deleted ? NoContent() : NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("reassign")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Reassign([FromBody] ReassignAccountantRequest request)
    {
        try
        {
            return Ok(await _assignments.ReassignAsync(request, _currentUserContextFactory.Create(User, HttpContext)));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("{id}/make-primary")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> MakePrimary(string id)
    {
        try
        {
            var updated = await _assignments.MakePrimaryAsync(id, _currentUserContextFactory.Create(User, HttpContext));
            return updated is null ? NotFound() : Ok(updated);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
