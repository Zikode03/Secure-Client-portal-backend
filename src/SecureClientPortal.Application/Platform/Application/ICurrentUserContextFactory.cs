using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace SecureClientPortal.Backend.Application;

public interface ICurrentUserContextFactory
{
    CurrentUserContext Create(ClaimsPrincipal user, HttpContext? httpContext = null);
}
