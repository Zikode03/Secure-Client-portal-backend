namespace SecureClientPortal.Backend.Models;

public class AuditLog
{
    public string Id { get; set; } = string.Empty;
    public string? ActorUserId { get; set; }
    public string ActorRole { get; set; } = "unknown";
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string? ClientId { get; set; }
    public string? MetadataJson { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
