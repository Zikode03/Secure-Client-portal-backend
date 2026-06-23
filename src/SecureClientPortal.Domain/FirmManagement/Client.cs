namespace SecureClientPortal.Backend.Models;

public class Client
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string EntityType { get; private set; } = string.Empty;
    public string Status { get; private set; } = ClientStatus.Active.ToStorageValue();
    public int ComplianceHealth { get; private set; }
    public string AssignedAccountantId { get; private set; } = string.Empty;
    public string PrimaryContact { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; private set; } = DateTime.UtcNow;

    public static Client Create(string id, string name, string entityType, string primaryContact, string email, ClientStatus status)
    {
        var client = new Client { Id = id, CreatedAtUtc = DateTime.UtcNow };
        client.UpdateDetails(name, entityType, primaryContact, email);
        client.ChangeStatus(status);
        return client;
    }

    public void UpdateDetails(string name, string entityType, string primaryContact, string email)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainRuleException("Client name is required.");
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

    public void AssignAccountant(string accountantUserId)
    {
        AssignedAccountantId = accountantUserId?.Trim() ?? string.Empty;
        Touch();
    }

    public void UpdateComplianceHealth(int complianceHealth)
    {
        ComplianceHealth = complianceHealth;
        Touch();
    }

    private void Touch()
    {
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
