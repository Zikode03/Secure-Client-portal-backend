using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SecureClientPortal.Backend.Api.Modules.Auth;

[ApiController]
[Route("api/auth/csrf")]
public sealed class CsrfController : ControllerBase
{
    /// <summary>Obtain a CSRF token bound to the current cookie session. Send it as X-CSRF-Token on writes.</summary>
    [HttpGet]
    [AllowAnonymous]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public ActionResult<CsrfTokenResponse> GetToken([FromServices] IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        Response.Headers.CacheControl = "no-store";
        return Ok(new CsrfTokenResponse(tokens.RequestToken ?? throw new InvalidOperationException("CSRF token generation failed.")));
    }
}

public sealed record CsrfTokenResponse(string RequestToken);
