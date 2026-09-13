namespace SecureClientPortal.Backend.Models;

public sealed class AccountSecurity
{
    public Guid Id { get; set; }
    public DateTime? LastResetRequestUtc { get; set; }
    public string? SmtpProbeHash { get; set; }
    public DateTime? SmtpProbeExpiresUtc { get; set; }
    public DateTime? SmtpVerifiedUtc { get; set; }
    public int FailedAttempts { get; set; }
    public DateTime? LockedUntilUtc { get; set; }
    public string? MfaSecret { get; set; }
    public string? PendingSecret { get; set; }
    public string? ChallengeHash { get; set; }
    public DateTime? ChallengeExpiresUtc { get; set; }
    public bool Persistent { get; set; }
    public long LastTotpStep { get; set; } = -1;
    public string RecoveryHashesJson { get; set; } = "[]";
    public Guid Version { get; set; } = Guid.NewGuid();

    public void Fail(DateTime now)
    {
        if (LockedUntilUtc <= now) { FailedAttempts = 0; LockedUntilUtc = null; }
        if (++FailedAttempts >= 5) LockedUntilUtc = now.AddMinutes(15);
        Version = Guid.NewGuid();
    }
    public void Succeed()
    {
        FailedAttempts = 0;
        LockedUntilUtc = null;
        Version = Guid.NewGuid();
    }
}
