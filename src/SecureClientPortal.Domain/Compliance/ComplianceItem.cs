namespace SecureClientPortal.Backend.Models;

public class ComplianceItem
{
    public string Id { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string CategoryId { get; set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Status { get; private set; } = ComplianceItemStatus.Missing.ToStorageValue();
    public string? OwnerUserId { get; private set; }
    public string RiskLevel { get; private set; } = ComplianceRiskLevel.Medium.ToStorageValue();
    public string? RequiredDocumentCategory { get; private set; }
    public string? LinkedDocumentId { get; private set; }
    public DateTime? DueDateUtc { get; private set; }
    public DateTime? ExpiryDateUtc { get; private set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; private set; } = DateTime.UtcNow;

    public static ComplianceItem Create(string id, string clientId, string categoryId, string name, ComplianceItemStatus status, string? ownerUserId, ComplianceRiskLevel riskLevel, string? requiredDocumentCategory, DateTime? dueDateUtc, DateTime? expiryDateUtc)
    {
        var item = new ComplianceItem
        {
            Id = id,
            ClientId = clientId,
            CategoryId = categoryId,
            CreatedAtUtc = DateTime.UtcNow
        };
        item.Update(name, status, ownerUserId, riskLevel, requiredDocumentCategory, null, dueDateUtc, expiryDateUtc);
        return item;
    }

    public void Update(string name, ComplianceItemStatus status, string? ownerUserId, ComplianceRiskLevel riskLevel, string? requiredDocumentCategory, string? linkedDocumentId, DateTime? dueDateUtc, DateTime? expiryDateUtc)
    {
        Name = name.Trim();
        Status = status.ToStorageValue();
        OwnerUserId = string.IsNullOrWhiteSpace(ownerUserId) ? null : ownerUserId.Trim();
        RiskLevel = riskLevel.ToStorageValue();
        RequiredDocumentCategory = string.IsNullOrWhiteSpace(requiredDocumentCategory) ? null : ComplianceDomainValues.NormalizeDocumentCategory(requiredDocumentCategory);
        LinkedDocumentId = string.IsNullOrWhiteSpace(linkedDocumentId) ? null : linkedDocumentId.Trim();
        DueDateUtc = dueDateUtc;
        ExpiryDateUtc = expiryDateUtc;
        Touch();
    }

    private void Touch()
    {
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
