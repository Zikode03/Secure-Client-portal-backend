namespace SecureClientPortal.Backend.Models;

public class ComplianceItem
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public Guid CategoryId { get; set; }
    public string Name { get; private set; } = string.Empty;
    public string Status { get; private set; } = ComplianceItemStatus.Missing.ToStorageValue();
    public Guid? OwnerUserId { get; private set; }
    public string RiskLevel { get; private set; } = ComplianceRiskLevel.Medium.ToStorageValue();
    public string? RequiredDocumentCategory { get; private set; }
    public Guid? LinkedDocumentId { get; private set; }
    public DateTime? DueDateUtc { get; private set; }
    public DateTime? ExpiryDateUtc { get; private set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; private set; } = DateTime.UtcNow;

    public static ComplianceItem Create(Guid id, Guid clientId, Guid categoryId, string name, ComplianceItemStatus status, Guid? ownerUserId, ComplianceRiskLevel riskLevel, string? requiredDocumentCategory, DateTime? dueDateUtc, DateTime? expiryDateUtc)
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

    public void Update(string name, ComplianceItemStatus status, Guid? ownerUserId, ComplianceRiskLevel riskLevel, string? requiredDocumentCategory, Guid? linkedDocumentId, DateTime? dueDateUtc, DateTime? expiryDateUtc)
    {
        Name = name.Trim();
        Status = status.ToStorageValue();
        OwnerUserId = ownerUserId == Guid.Empty ? null : ownerUserId;
        RiskLevel = riskLevel.ToStorageValue();
        RequiredDocumentCategory = string.IsNullOrWhiteSpace(requiredDocumentCategory) ? null : ComplianceDomainValues.NormalizeDocumentCategory(requiredDocumentCategory);
        LinkedDocumentId = linkedDocumentId == Guid.Empty ? null : linkedDocumentId;
        DueDateUtc = dueDateUtc;
        ExpiryDateUtc = expiryDateUtc;
        Touch();
    }

    private void Touch()
    {
        UpdatedAtUtc = DateTime.UtcNow;
    }
}






