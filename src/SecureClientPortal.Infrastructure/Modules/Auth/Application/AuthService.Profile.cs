using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts.Modules.Auth;
using SecureClientPortal.Backend.Data;

namespace SecureClientPortal.Backend.Infrastructure.Modules.Auth.Application;

public sealed partial class AuthService
{
    public async Task<ServiceResult<object>> UpdateProfileAsync(UpdateProfileRequest request, ClaimsPrincipal actor, CancellationToken ct = default)
    {
        var identity = await ResolveIdentityAsync(actor, ct);
        if (identity.Error is not null) return identity.Error;

        var fullName = request.FullName?.Trim() ?? "";
        var title = request.Title?.Trim() ?? "";
        var phone = request.Phone?.Trim() ?? "";
        if (fullName.Length is 0 or > 200 || title.Length > 100 || phone.Length > 40)
            return ServiceResult<object>.ErrorResult("Enter a full name (up to 200 characters), job title (up to 100), and phone number (up to 40).", "INVALID_PROFILE");
        if (fullName.Any(char.IsControl) || title.Any(char.IsControl) || phone.Any(char.IsControl))
            return ServiceResult<object>.ErrorResult("Profile fields cannot contain control characters.", "INVALID_PROFILE");

        var user = identity.User!;
        // Preserve administrator-owned metadata; only these personal fields are editable.
        var profile = ReadPersonalProfile(user.ProfileJson);
        profile["title"] = title;
        profile["phone"] = phone;
        user.SetFullName(fullName);
        user.SetProfileJson(profile.ToJsonString());
        await _db.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(actor, "user.profile_updated", "user", user.Id, null,
            JsonSerializer.Serialize(new { fields = new[] { "fullName", "title", "phone" } }), ct);
        return await MeAsync(actor, ct);
    }

    private static JsonObject ReadPersonalProfile(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new JsonObject();
        // Invalid stored data must not be silently discarded during a profile edit.
        return JsonNode.Parse(json) as JsonObject ?? throw new InvalidOperationException("Invalid stored profile.");
    }

    private static string PersonalProfileField(string? json, string field)
    {
        if (string.IsNullOrWhiteSpace(json)) return "";
        try
        {
            var profile = JsonNode.Parse(json) as JsonObject;
            return profile?[field] is JsonValue value && value.TryGetValue<string>(out var text) ? text : "";
        }
        catch (JsonException) { return ""; }
    }
}
