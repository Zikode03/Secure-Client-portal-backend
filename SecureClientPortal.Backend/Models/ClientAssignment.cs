namespace SecureClientPortal.Backend.Models;

public class ClientAssignment
{
    public string Id { get; set; } = string.Empty;
    public string AccountantUserId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
