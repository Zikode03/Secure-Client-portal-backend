namespace SecureClientPortal.Backend.Models;

public sealed class ComplianceAssessmentDomainService
{
    public ComplianceAssessment Assess(IReadOnlyCollection<ComplianceItem> items)
    {
        var total = items.Count;
        var valid = items.Count(x => x.Status == "valid");
        var expiringSoon = items.Count(x => x.Status == "expiring_soon");
        var expired = items.Count(x => x.Status == "expired");
        var missing = items.Count(x => x.Status == "missing");
        var pending = items.Count(x => x.Status == "pending");
        var rejected = items.Count(x => x.Status == "rejected");
        var criticalRisk = items.Count(x => x.RiskLevel == "critical");
        var highRisk = items.Count(x => x.RiskLevel == "high");
        var complianceScore = total == 0
            ? 0
            : (int)Math.Round((double)valid / total * 100);

        return new ComplianceAssessment(
            total,
            valid,
            expiringSoon,
            expired,
            missing,
            pending,
            rejected,
            criticalRisk,
            highRisk,
            complianceScore);
    }
}
