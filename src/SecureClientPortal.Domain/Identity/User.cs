using SecureClientPortal.Backend.Auth;

namespace SecureClientPortal.Backend.Models;

public class User
{
    public string Id { get; set; } = string.Empty;
    public string FullName { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public string Role { get; private set; } = UserRole.Client.ToStorageValue();
    public string ClientIdsJson { get; private set; } = "[]";
    public string? ProfileJson { get; private set; }
    public string? SecurityJson { get; private set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; private set; } = DateTime.UtcNow;

    public static User CreateInvited(string id, string fullName, string email, UserRole role, string passwordHash, string clientIdsJson, string? profileJson)
    {
        var user = new User { Id = id, CreatedAtUtc = DateTime.UtcNow };
        user.SetFullName(fullName);
        user.Email = EmailAddress.Parse(email);
        user.PasswordHash = passwordHash;
        user.Role = role.ToStorageValue();
        user.ClientIdsJson = string.IsNullOrWhiteSpace(clientIdsJson) ? "[]" : clientIdsJson;
        user.ProfileJson = profileJson;
        user.SecurityJson = UserSecurityProfile.SetStatus(null, SecurityStatus.Invited.ToStorageValue());
        user.Touch();
        return user;
    }

    public void CompleteSetup(string fullName, string passwordHash)
    {
        SetFullName(fullName);
        PasswordHash = passwordHash;
        SecurityJson = UserSecurityProfile.SetStatus(SecurityJson, SecurityStatus.Active.ToStorageValue());
        Touch();
    }

    public void SetFullName(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) throw new DomainRuleException("Full name is required.");
        FullName = fullName.Trim();
        Touch();
    }

    public void AssignRole(UserRole role)
    {
        Role = role.ToStorageValue();
        Touch();
    }

    public void SetClientIdsJson(string clientIdsJson)
    {
        ClientIdsJson = string.IsNullOrWhiteSpace(clientIdsJson) ? "[]" : clientIdsJson;
        Touch();
    }

    public void SetProfileJson(string? profileJson)
    {
        ProfileJson = string.IsNullOrWhiteSpace(profileJson) ? null : profileJson;
        Touch();
    }

    public void SetPasswordHash(string passwordHash)
    {
        PasswordHash = passwordHash;
        Touch();
    }

    public void SetSecurityStatus(SecurityStatus status, string? reason = null)
    {
        SecurityJson = UserSecurityProfile.SetStatus(SecurityJson, status.ToStorageValue(), reason);
        Touch();
    }

    public void RecordActivity()
    {
        Touch();
    }

    public void SetEmail(string email)
    {
        Email = EmailAddress.Parse(email);
        Touch();
    }

    private void Touch()
    {
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
