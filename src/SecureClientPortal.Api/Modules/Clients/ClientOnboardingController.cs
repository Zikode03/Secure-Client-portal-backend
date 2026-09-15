using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts.Modules.Clients;
using SecureClientPortal.Backend.Application.Modules.Clients;

namespace SecureClientPortal.Backend.Api.Modules.Clients;

[ApiController]
[Route("api/clients/onboarding")]
[Authorize(Policy = "AdminOnly")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ClientOnboardingController(IClientOnboardingService service) : ControllerBase
{
    [HttpGet("options")]
    public async Task<IActionResult> Options(CancellationToken ct) => Result(await service.GetOptionsAsync(User, ct));
    [HttpPost]
    public async Task<IActionResult> Create(OnboardClientRequest request, CancellationToken ct) => Result(await service.CreateAsync(request, User, ct));
    private IActionResult Result<T>(ServiceResult<T> result)
    {
        if (result.Forbidden) return Forbid();
        if (result.Error is not null) return StatusCode(result.StatusCode ?? 400, new { error = result.Error });
        return Ok(result.Value);
    }
}

