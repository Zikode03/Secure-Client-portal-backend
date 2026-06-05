using System.Text.Json;
using System.Text.Json.Nodes;

namespace SecureClientPortal.Backend.Auth;

public static class UserSecurityProfile
{
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
        JsonObject node;
        try
        {
            node = JsonNode.Parse(string.IsNullOrWhiteSpace(securityJson) ? "{}" : securityJson)?.AsObject() ?? [];
        }
        catch
        {
            node = [];
        }

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
}
