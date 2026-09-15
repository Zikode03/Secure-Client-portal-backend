using System.Security.Claims;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts.Modules.Clients;

namespace SecureClientPortal.Backend.Application.Modules.Clients;

public interface IClientOnboardingService
{
    Task<ServiceResult<ClientOnboardingOptions>> GetOptionsAsync(ClaimsPrincipal actor, CancellationToken ct);
    Task<ServiceResult<ClientOnboardingResponse>> CreateAsync(OnboardClientRequest request, ClaimsPrincipal actor, CancellationToken ct);
}

