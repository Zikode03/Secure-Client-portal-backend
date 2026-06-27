namespace SecureClientPortal.Backend.Models;

public class UserSession
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid JwtId { get; private set; }
    public DateTime IssuedAtUtc { get; private set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }
    public string? RevokedReason { get; private set; }
    public string? ClientIp { get; private set; }
    public string? UserAgent { get; private set; }

    public static UserSession Start(Guid id, Guid userId, Guid jwtId, DateTime expiresAtUtc, string? clientIp, string? userAgent)
    {
        if (id == Guid.Empty) throw new DomainRuleException("Session id is required.");
        if (userId == Guid.Empty) throw new DomainRuleException("User id is required.");
        if (jwtId == Guid.Empty) throw new DomainRuleException("Jwt id is required.");

        var issuedAtUtc = DateTime.UtcNow;
        return new UserSession
        {
            Id = id,
            UserId = userId,
            JwtId = jwtId,
            IssuedAtUtc = issuedAtUtc,
            ExpiresAtUtc = expiresAtUtc,
            ClientIp = string.IsNullOrWhiteSpace(clientIp) ? null : clientIp.Trim(),
            UserAgent = string.IsNullOrWhiteSpace(userAgent) ? null : userAgent.Trim(),
            RevokedAtUtc = null,
            RevokedReason = null
        };
    }

    public void Refresh(Guid jwtId, DateTime expiresAtUtc, string? clientIp, string? userAgent)
    {
        if (jwtId == Guid.Empty) throw new DomainRuleException("Jwt id is required.");
        JwtId = jwtId;
        IssuedAtUtc = DateTime.UtcNow;
        ExpiresAtUtc = expiresAtUtc;
        ClientIp = string.IsNullOrWhiteSpace(clientIp) ? null : clientIp.Trim();
        UserAgent = string.IsNullOrWhiteSpace(userAgent) ? null : userAgent.Trim();
        RevokedAtUtc = null;
        RevokedReason = null;
    }

    public void Revoke(string reason)
    {
        if (RevokedAtUtc is not null)
        {
            return;
        }

        RevokedAtUtc = DateTime.UtcNow;
        RevokedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }
}
