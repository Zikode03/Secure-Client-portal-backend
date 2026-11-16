using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using SecureClientPortal.Backend.Auth;

namespace SecureClientPortal.Backend.Api.Security;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class RequirePermissionAttribute(string permission) : Attribute, IAuthorizationFilter
{
    public string Permission { get; } = permission;
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (context.HttpContext.User.Identity?.IsAuthenticated != true) context.Result = new UnauthorizedResult();
        else if (!context.HttpContext.User.HasPermission(Permission)) context.Result = new ForbidResult();
    }
}
