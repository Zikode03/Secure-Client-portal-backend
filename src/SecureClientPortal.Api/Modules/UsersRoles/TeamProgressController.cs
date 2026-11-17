using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureClientPortal.Backend.Application.Contracts.Modules.UsersRoles;
using SecureClientPortal.Backend.Infrastructure.Modules.UsersRoles.Application;
namespace SecureClientPortal.Backend.Api.Modules.UsersRoles;
[ApiController, Route("api/admin/team-progress"), Authorize(Policy = "AdminOnly")]
[SecureClientPortal.Backend.Api.Security.RequirePermission("assignments.read")]
[SecureClientPortal.Backend.Api.Security.RequirePermission("users.read")]
[SecureClientPortal.Backend.Api.Security.RequirePermission("documents.read")]
[SecureClientPortal.Backend.Api.Security.RequirePermission("audit_logs.read")]
public sealed class TeamProgressController(TeamProgressService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<TeamProgressResponse>> Get(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(await service.GetAsync(ct));
    }
}
