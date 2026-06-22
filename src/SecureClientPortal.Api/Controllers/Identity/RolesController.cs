using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureClientPortal.Backend.Application;
using SecureClientPortal.Backend.Application.Contracts;
using SecureClientPortal.Backend.Application.Roles;

namespace SecureClientPortal.Backend.Controllers;

[ApiController]
[Route("api/roles")]
[Authorize(Policy = "AdminOnly")]
public class RolesController : ControllerBase
{
    private readonly IRoleService _roles;
    private readonly ICurrentUserContextFactory _currentUserContextFactory;

    public RolesController(IRoleService roles, ICurrentUserContextFactory currentUserContextFactory)
    {
        _roles = roles;
        _currentUserContextFactory = currentUserContextFactory;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<object>>> GetAll()
    {
        return Ok(await _roles.GetAllAsync());
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRoleRequest request)
    {
        try
        {
            var actor = _currentUserContextFactory.Create(User, HttpContext);
            var created = await _roles.CreateAsync(request, actor);
            return Created("/api/roles", created);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    [HttpPut("{name}")]
    public async Task<IActionResult> Update(string name, [FromBody] UpdateRoleRequest request)
    {
        try
        {
            var actor = _currentUserContextFactory.Create(User, HttpContext);
            var updated = await _roles.UpdateAsync(name, request, actor);
            return updated is null ? NotFound() : Ok(updated);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("{name}/activate")]
    public async Task<IActionResult> Activate(string name)
    {
        return await UpdateActivationAsync(name, true);
    }

    [HttpPost("{name}/deactivate")]
    public async Task<IActionResult> Deactivate(string name)
    {
        return await UpdateActivationAsync(name, false);
    }

    private async Task<IActionResult> UpdateActivationAsync(string name, bool isActive)
    {
        var actor = _currentUserContextFactory.Create(User, HttpContext);
        var updated = await _roles.UpdateActivationAsync(name, isActive, actor);
        return updated is null ? NotFound() : Ok(updated);
    }
}
