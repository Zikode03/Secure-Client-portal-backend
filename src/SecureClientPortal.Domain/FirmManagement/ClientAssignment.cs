namespace SecureClientPortal.Backend.Models;

public class ClientAssignment
{
    public string Id { get; private set; } = string.Empty;
    public string AccountantUserId { get; private set; } = string.Empty;
    public string ClientId { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;

    public static ClientAssignment Create(
        string id,
        string accountantUserId,
        string clientId,
        DateTime? createdAtUtc = null)
    {
        return new ClientAssignment
        {
            Id = id,
            AccountantUserId = string.IsNullOrWhiteSpace(accountantUserId) ? throw new ArgumentException("Accountant user id is required.", nameof(accountantUserId)) : accountantUserId.Trim(),
            ClientId = string.IsNullOrWhiteSpace(clientId) ? throw new ArgumentException("Client id is required.", nameof(clientId)) : clientId.Trim(),
            CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow
        };
    }
}
