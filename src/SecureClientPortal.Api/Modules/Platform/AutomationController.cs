using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureClientPortal.Backend.Application.Modules.Platform;

namespace SecureClientPortal.Backend.Api.Modules.Platform;

[ApiController]
[Route("api/platform/automation")]
[Authorize(Policy = "AdminOnly")]
public class AutomationController : ControllerBase
{
    private readonly IAutomationWorkflowService _automationWorkflowService;

    public AutomationController(IAutomationWorkflowService automationWorkflowService)
    {
        _automationWorkflowService = automationWorkflowService;
    }

    [HttpPost("run")]
    public async Task<IActionResult> Run(CancellationToken ct)
    {
        return Ok(await _automationWorkflowService.RunAsync(null, ct));
    }
}
