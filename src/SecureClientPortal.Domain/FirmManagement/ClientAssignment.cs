namespace SecureClientPortal.Backend.Models;

public class ClientAssignment
{
    public Guid Id { get; private set; }
    public Guid AccountantUserId { get; private set; }
    public Guid ClientId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;

    public static ClientAssignment Create(
        Guid id,
        Guid accountantUserId,
        Guid clientId,
        DateTime? createdAtUtc = null)
    {
        return new ClientAssignment
        {
            Id = id,
            AccountantUserId = accountantUserId == Guid.Empty ? throw new ArgumentException("Accountant user id is required.", nameof(accountantUserId)) : accountantUserId,
            ClientId = clientId == Guid.Empty ? throw new ArgumentException("Client id is required.", nameof(clientId)) : clientId,
            CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow
        };
    }
}





