namespace SecureClientPortal.Backend.Models;

public class ComplianceItem
{
    public Guid Id { get; private set; }
    public Guid ClientId { get; private set; }
    public Guid CategoryId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Status { get; private set; } = ComplianceItemStatus.Missing.ToStorageValue();
    public Guid? OwnerUserId { get; private set; }
    public string RiskLevel { get; private set; } = ComplianceRiskLevel.Medium.ToStorageValue();
    public string? RequiredDocumentCategory { get; private set; }
    public Guid? LinkedDocumentId { get; private set; }
    public DateTime? DueDateUtc { get; private set; }
    public DateTime? ExpiryDateUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; private set; } = DateTime.UtcNow;

    public static ComplianceItem Create(Guid id, Guid clientId, Guid categoryId, string name, ComplianceItemStatus status, Guid? ownerUserId, ComplianceRiskLevel riskLevel, string? requiredDocumentCategory, DateTime? dueDateUtc, DateTime? expiryDateUtc, DateTime? createdAtUtc = null)
    {
        if (id == Guid.Empty) throw new DomainRuleException("Compliance item id is required.");
        if (clientId == Guid.Empty) throw new DomainRuleException("Client id is required.");
        if (categoryId == Guid.Empty) throw new DomainRuleException("Compliance category id is required.");

        var created = createdAtUtc ?? DateTime.UtcNow;
        var item = new ComplianceItem
        {
            Id = id,
            ClientId = clientId,
            CategoryId = categoryId,
            CreatedAtUtc = created,
            UpdatedAtUtc = created
        };

        item.Update(name, status, ownerUserId, riskLevel, requiredDocumentCategory, null, dueDateUtc, expiryDateUtc);
        return item;
    }

    public void Update(string name, ComplianceItemStatus status, Guid? ownerUserId, ComplianceRiskLevel riskLevel, string? requiredDocumentCategory, Guid? linkedDocumentId, DateTime? dueDateUtc, DateTime? expiryDateUtc)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainRuleException("Compliance item name is required.");

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

    public bool IsExpiredAt(DateTime now) => ExpiryDateUtc.HasValue && ExpiryDateUtc.Value.Date < now.Date;

    private void Touch()
    {
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
