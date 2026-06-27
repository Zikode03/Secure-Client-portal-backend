namespace SecureClientPortal.Backend.Models;

public class UserAccessToken
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string Purpose { get; private set; } = string.Empty;
    public string TokenHash { get; private set; } = string.Empty;
    public Guid? SessionId { get; private set; }
    public Guid? CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime? ConsumedAtUtc { get; private set; }
    public DateTime? InvalidatedAtUtc { get; private set; }
    public string? InvalidatedReason { get; private set; }

    public static UserAccessToken Create(Guid id, Guid userId, string purpose, string tokenHash, DateTime expiresAtUtc, Guid? sessionId = null, Guid? createdByUserId = null, DateTime? createdAtUtc = null)
    {
        if (id == Guid.Empty) throw new DomainRuleException("Access token id is required.");
        if (userId == Guid.Empty) throw new DomainRuleException("User id is required.");
        if (string.IsNullOrWhiteSpace(purpose)) throw new DomainRuleException("Access token purpose is required.");
        if (string.IsNullOrWhiteSpace(tokenHash)) throw new DomainRuleException("Access token hash is required.");

        return new UserAccessToken
        {
            Id = id,
            UserId = userId,
            Purpose = purpose.Trim(),
            TokenHash = tokenHash,
            SessionId = sessionId == Guid.Empty ? null : sessionId,
            CreatedByUserId = createdByUserId == Guid.Empty ? null : createdByUserId,
            CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow,
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
