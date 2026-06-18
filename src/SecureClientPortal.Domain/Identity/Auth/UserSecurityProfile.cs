using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.Text;

namespace SecureClientPortal.Backend.Auth;

public static class UserSecurityProfile
{
    private const string OneTimeTokenHashKey = "oneTimeTokenHash";
    private const string OneTimeTokenPurposeKey = "oneTimeTokenPurpose";
    private const string OneTimeTokenExpiresAtUtcKey = "oneTimeTokenExpiresAtUtc";

    public static string GetStatus(string? securityJson)
    {
        if (string.IsNullOrWhiteSpace(securityJson))
        {
            return "active";
        }

        try
        {
            var node = JsonNode.Parse(securityJson);
            return node?["status"]?.GetValue<string>()?.Trim().ToLowerInvariant() ?? "active";
        }
        catch
        {
            return "active";
        }
    }

    public static string SetStatus(string? securityJson, string status, string? reason = null)
    {
        var node = ParseObject(securityJson);

        node["status"] = status.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(reason))
        {
            node["reason"] = reason.Trim();
        }
        else if (node.ContainsKey("reason"))
        {
            node.Remove("reason");
        }

        return node.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    public static string SetOneTimeToken(string? securityJson, string token, string purpose, DateTime expiresAtUtc)
    {
        var node = ParseObject(securityJson);
        node[OneTimeTokenHashKey] = HashToken(token);
        node[OneTimeTokenPurposeKey] = purpose.Trim().ToLowerInvariant();
        node[OneTimeTokenExpiresAtUtcKey] = expiresAtUtc.ToUniversalTime().ToString("O");
        return node.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    public static string ClearOneTimeToken(string? securityJson)
    {
        var node = ParseObject(securityJson);
        node.Remove(OneTimeTokenHashKey);
        node.Remove(OneTimeTokenPurposeKey);
        node.Remove(OneTimeTokenExpiresAtUtcKey);
        return node.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    public static bool HasMatchingOneTimeToken(
        string? securityJson,
        string token,
        IReadOnlyCollection<string> allowedPurposes,
        DateTime utcNow,
        out string error)
    {
        error = "Invite token is invalid or expired.";
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        try
        {
            var node = ParseObject(securityJson);
            var storedHash = node[OneTimeTokenHashKey]?.GetValue<string>();
            var storedPurpose = node[OneTimeTokenPurposeKey]?.GetValue<string>()?.Trim().ToLowerInvariant();
            var expiresRaw = node[OneTimeTokenExpiresAtUtcKey]?.GetValue<string>();

            if (string.IsNullOrWhiteSpace(storedHash) || string.IsNullOrWhiteSpace(storedPurpose) || string.IsNullOrWhiteSpace(expiresRaw))
            {
                return false;
            }

            if (!allowedPurposes.Contains(storedPurpose, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!DateTime.TryParse(expiresRaw, out var expiresAtUtc) || expiresAtUtc <= utcNow.ToUniversalTime())
            {
                error = "Invite token has expired.";
                return false;
            }

            return string.Equals(storedHash, HashToken(token), StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static JsonObject ParseObject(string? securityJson)
    {
        try
        {
            return JsonNode.Parse(string.IsNullOrWhiteSpace(securityJson) ? "{}" : securityJson)?.AsObject() ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static string HashToken(string token)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hash);
    }
}
