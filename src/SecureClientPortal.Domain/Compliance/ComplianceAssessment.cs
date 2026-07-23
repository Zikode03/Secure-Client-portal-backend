namespace SecureClientPortal.Backend.Models;

public sealed record ComplianceAssessment(
    int Total,
    int Valid,
    int ExpiringSoon,
    int Expired,
    int Missing,
    int Pending,
    int Rejected,
    int CriticalRisk,
    int HighRisk,
    int ComplianceScore);
