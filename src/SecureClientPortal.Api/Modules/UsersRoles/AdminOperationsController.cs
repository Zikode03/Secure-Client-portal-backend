using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureClientPortal.Backend.Application.Contracts.Modules.UsersRoles;
using SecureClientPortal.Backend.Infrastructure.Modules.UsersRoles.Application;
namespace SecureClientPortal.Backend.Api.Modules.UsersRoles;
[ApiController, Route("api/admin/operations"), Authorize(Policy = "AdminOnly")]
[SecureClientPortal.Backend.Api.Security.RequirePermission("settings.read")]
public sealed class AdminOperationsController(AdminOperationsService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<AdminOperationsResponse>> Get(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(await service.GetAsync(ct));
    }
}
