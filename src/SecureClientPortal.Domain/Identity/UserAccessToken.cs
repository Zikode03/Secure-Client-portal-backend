namespace SecureClientPortal.Backend.Models;

public class UserAccessToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; private set; }
    public string Purpose { get; private set; } = string.Empty;
    public string TokenHash { get; private set; } = string.Empty;
    public Guid? SessionId { get; private set; }
    public Guid? CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime? ConsumedAtUtc { get; private set; }
    public DateTime? InvalidatedAtUtc { get; private set; }
    public string? InvalidatedReason { get; private set; }

    public static UserAccessToken Create(Guid id, Guid userId, string purpose, string tokenHash, DateTime expiresAtUtc, Guid? sessionId = null, Guid? createdByUserId = null)
    {
        return new UserAccessToken
        {
            Id = id,
            UserId = userId,
            Purpose = purpose.Trim(),
            TokenHash = tokenHash,
            SessionId = sessionId == Guid.Empty ? null : sessionId,
            CreatedByUserId = createdByUserId == Guid.Empty ? null : createdByUserId,
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






