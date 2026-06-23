namespace SecureClientPortal.Backend.Models;

public class UserSession
{
    public Guid Id { get; set; }
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





