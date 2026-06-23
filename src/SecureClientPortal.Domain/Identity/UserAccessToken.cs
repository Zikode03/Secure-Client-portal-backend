namespace SecureClientPortal.Backend.Models;

public class UserAccessToken
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; private set; } = string.Empty;
    public string Purpose { get; private set; } = string.Empty;
    public string TokenHash { get; private set; } = string.Empty;
    public string? SessionId { get; private set; }
    public string? CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime? ConsumedAtUtc { get; private set; }
    public DateTime? InvalidatedAtUtc { get; private set; }
    public string? InvalidatedReason { get; private set; }

    public static UserAccessToken Create(string id, string userId, string purpose, string tokenHash, DateTime expiresAtUtc, string? sessionId = null, string? createdByUserId = null)
    {
        return new UserAccessToken
        {
            Id = id,
            UserId = userId,
            Purpose = purpose.Trim(),
            TokenHash = tokenHash,
            SessionId = string.IsNullOrWhiteSpace(sessionId) ? null : sessionId.Trim(),
            CreatedByUserId = string.IsNullOrWhiteSpace(createdByUserId) ? null : createdByUserId.Trim(),
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = expiresAtUtc
        };
    }

    public void Consume()
    {
        if (ConsumedAtUtc is not null)
        {
            return;
        }

        ConsumedAtUtc = DateTime.UtcNow;
    }

    public void Invalidate(string reason)
    {
        if (InvalidatedAtUtc is not null)
        {
            return;
        }

        InvalidatedAtUtc = DateTime.UtcNow;
        InvalidatedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }
}
