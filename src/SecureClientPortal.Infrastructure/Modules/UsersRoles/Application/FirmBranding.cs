using System.Text.Json;
using SecureClientPortal.Backend.Application.Common;
namespace SecureClientPortal.Backend.Infrastructure.Modules.UsersRoles.Application;
public static class FirmBranding
{
    public static void Validate(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new AppValidationException("Firm profile must be an object.");
            if (doc.RootElement.TryGetProperty("firmName", out var name) && (name.ValueKind != JsonValueKind.String || name.GetString()!.Length > 160)) throw new AppValidationException("Firm name must be text up to 160 characters.");
            if (!doc.RootElement.TryGetProperty("logoDataUrl", out var logo) || logo.ValueKind == JsonValueKind.Null || logo.GetString() == "") return;
            var value = logo.GetString()!;
            var png = value.StartsWith("data:image/png;base64,", StringComparison.Ordinal);
            var jpg = value.StartsWith("data:image/jpeg;base64,", StringComparison.Ordinal);
            if ((!png && !jpg) || value.Length > 140_000) throw new AppValidationException("Use a PNG or JPEG logo up to 100 KB.");
            var bytes = Convert.FromBase64String(value[(value.IndexOf(',') + 1)..]);
            if (bytes.Length > 100_000 || !(png ? bytes.AsSpan().StartsWith(new byte[] {137,80,78,71,13,10,26,10}) : bytes.AsSpan().StartsWith(new byte[] {255,216,255}))) throw new AppValidationException("Logo contents do not match its file type.");
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException) { throw new AppValidationException("Firm profile or logo is invalid."); }
    }
}
