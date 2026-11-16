using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureClientPortal.Backend.Data;
using System.Text.Json;
namespace SecureClientPortal.Backend.Api.Modules.FirmManagement;
[ApiController, Route("api/firm-branding"), Authorize]
public sealed class FirmBrandingController(PortalDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<FirmBrandingResponse>> Get(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var profile = await db.SystemSettings.FindAsync(["firm.profile"], ct);
        try
        {
            using var json = JsonDocument.Parse(profile?.ValueJson ?? "{}");
            var root = json.RootElement;
            string? Text(string key) => root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            // Only validated inline images are exposed; never a remote tracking URL or SVG.
            var logo = Text("logoDataUrl");
            try { SecureClientPortal.Backend.Infrastructure.Modules.UsersRoles.Application.FirmBranding.Validate(profile?.ValueJson ?? "{}"); }
            catch { logo = null; }
            return Ok(new FirmBrandingResponse(Text("firmName") ?? "", logo));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return Ok(new FirmBrandingResponse("", null)); }
    }
}
public sealed record FirmBrandingResponse(string FirmName, string? LogoDataUrl);
