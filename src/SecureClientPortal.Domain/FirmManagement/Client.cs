namespace SecureClientPortal.Backend.Models;

public class Client
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string EntityType { get; private set; } = string.Empty;
    public string Status { get; private set; } = ClientStatus.Active.ToStorageValue();
    public int ComplianceHealth { get; private set; }
    public Guid AssignedAccountantId { get; private set; }
    public string PrimaryContact { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; private set; } = DateTime.UtcNow;

    public static Client Create(Guid id, string name, string entityType, string primaryContact, string email, ClientStatus status, DateTime? createdAtUtc = null)
    {
        if (id == Guid.Empty) throw new DomainRuleException("Client id is required.");

        var client = new Client
        {
            Id = id,
            CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow,
            UpdatedAtUtc = createdAtUtc ?? DateTime.UtcNow
        };

        client.UpdateDetails(name, entityType, primaryContact, email);
        client.ChangeStatus(status);
        return client;
    }

    public void UpdateDetails(string name, string entityType, string primaryContact, string email)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainRuleException("Client name is required.");
        if (string.IsNullOrWhiteSpace(entityType)) throw new DomainRuleException("Client entity type is required.");
        if (string.IsNullOrWhiteSpace(primaryContact)) throw new DomainRuleException("Primary contact is required.");

        Name = name.Trim();
        EntityType = entityType.Trim();
        PrimaryContact = primaryContact.Trim();
        Email = EmailAddress.Parse(email);
        Touch();
    }

    public void ChangeStatus(ClientStatus status)
    {
        Status = status.ToStorageValue();
        Touch();
    }

    public void AssignAccountant(Guid accountantUserId)
    {
        if (accountantUserId == Guid.Empty) throw new DomainRuleException("Assigned accountant id is required.");
        AssignedAccountantId = accountantUserId;
        Touch();
    }

    public void UpdateComplianceHealth(int complianceHealth)
    {
        if (complianceHealth is < 0 or > 100) throw new DomainRuleException("Compliance health must be between 0 and 100.");
        ComplianceHealth = complianceHealth;
        Touch();
    }

    private void Touch()
    {
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
