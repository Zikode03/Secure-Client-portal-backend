namespace SecureClientPortal.Backend.Application.Modules.Compliance;

public sealed record AuthorityVerificationResult(
    bool Success,
    string Outcome,
    string EvidenceReference,
    DateTime CheckedAtUtc,
    DateTime ReviewAfterUtc,
    string? Error = null);

public interface ICipcAuthorityClient
{
    bool IsConfigured(string checkCode);
    Task<AuthorityVerificationResult> VerifyAsync(string checkCode, string registrationNumber, CancellationToken ct);
}
