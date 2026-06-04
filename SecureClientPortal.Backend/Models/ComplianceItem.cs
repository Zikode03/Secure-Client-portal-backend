namespace SecureClientPortal.Backend.Models;

public class ComplianceItem
{
    public string Id { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string CategoryId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = "missing";
    public string? OwnerUserId { get; set; }
    public string RiskLevel { get; set; } = "medium";
    public string? RequiredDocumentCategory { get; set; }
    public string? LinkedDocumentId { get; set; }
    public DateTime? DueDateUtc { get; set; }
    public DateTime? ExpiryDateUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
