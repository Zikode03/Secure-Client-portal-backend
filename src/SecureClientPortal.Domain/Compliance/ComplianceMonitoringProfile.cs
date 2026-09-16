namespace SecureClientPortal.Backend.Models;

// Configuration is deliberately separate from document status and authority results.
public sealed class ComplianceMonitoringProfile
{
    public Guid ClientId { get; private set; }
    public string CsdSupplierNumber { get; private set; } = "";
    public Guid Version { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static ComplianceMonitoringProfile Create(Guid clientId) => new() { ClientId = clientId };
    public void Update(string csdSupplierNumber)
    {
        CsdSupplierNumber = csdSupplierNumber;
        Version = Guid.NewGuid();
        UpdatedAtUtc = DateTime.UtcNow;
    }
}

public sealed class ComplianceCheckSetting
{
    public Guid ClientId { get; private set; }
    public string CheckCode { get; private set; } = "";
    public string Applicability { get; private set; } = "undecided";
    public string Reason { get; private set; } = "";

    public static ComplianceCheckSetting Create(Guid clientId, string checkCode) => new() { ClientId = clientId, CheckCode = checkCode };
    public void Update(string applicability, string reason) { Applicability = applicability; Reason = reason; }
}

// Append-only observations. Manual callers cannot create authority-verified records.
public sealed class ComplianceVerification
{
    public Guid Id { get; private set; }
    public Guid ClientId { get; private set; }
    public string CheckCode { get; private set; } = "";
    public string Method { get; private set; } = "accountant_confirmed";
    public string Outcome { get; private set; } = "unknown";
    public string EvidenceReference { get; private set; } = "";
    public DateTime CheckedAtUtc { get; private set; }
    public DateTime RecordedAtUtc { get; private set; }
    public DateTime ReviewAfterUtc { get; private set; }
    public Guid RecordedByUserId { get; private set; }
    // Binds the observation to the identifiers used, without duplicating them in history.
    public string IdentifierFingerprint { get; private set; } = "";

    public static ComplianceVerification RecordManual(Guid clientId, string code, string outcome,
        string evidenceReference, DateTime checkedAt, DateTime reviewAfter, Guid actorId, string fingerprint) => new()
    {
        Id = Guid.NewGuid(), ClientId = clientId, CheckCode = code, Method = "accountant_confirmed", Outcome = outcome,
        EvidenceReference = evidenceReference, CheckedAtUtc = checkedAt, ReviewAfterUtc = reviewAfter,
        RecordedAtUtc = DateTime.UtcNow, RecordedByUserId = actorId, IdentifierFingerprint = fingerprint
    };

    public static ComplianceVerification RecordAuthority(Guid clientId, string code, string outcome,
        string evidenceReference, DateTime checkedAt, DateTime reviewAfter, Guid actorId, string fingerprint) => new()
    {
        Id = Guid.NewGuid(), ClientId = clientId, CheckCode = code, Method = "authority_verified", Outcome = outcome,
        EvidenceReference = evidenceReference, CheckedAtUtc = checkedAt, ReviewAfterUtc = reviewAfter,
        RecordedAtUtc = DateTime.UtcNow, RecordedByUserId = actorId, IdentifierFingerprint = fingerprint
    };
}
