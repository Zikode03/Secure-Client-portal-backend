using Microsoft.Extensions.Options;
using SecureClientPortal.Backend.Application.Identity;

namespace SecureClientPortal.Backend.Auth;

public class AccessLinkBuilder : IAccessLinkBuilder
{
    private readonly PortalLinksOptions _options;

    public AccessLinkBuilder(IOptions<PortalLinksOptions> options)
    {
        _options = options.Value;
    }

    public string BuildSetupUrl(string email, string token)
    {
        return BuildUrl(email, token, "invite");
    }

    public string BuildPasswordResetUrl(string email, string token)
    {
        return BuildUrl(email, token, "reset");
    }

    private string BuildUrl(string email, string token, string mode)
    {
        var baseUrl = (_options.FrontendBaseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            baseUrl = "http://localhost:5173";
        }

        return $"{baseUrl}/invite-setup?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}&mode={Uri.EscapeDataString(mode)}";
    }
}
