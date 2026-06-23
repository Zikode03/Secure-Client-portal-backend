namespace SecureClientPortal.Backend.Models;

public class AuditLog
{
    public string Id { get; private set; } = string.Empty;
    public string? ActorUserId { get; private set; }
    public string ActorRole { get; private set; } = "unknown";
    public string Action { get; private set; } = string.Empty;
    public string EntityType { get; private set; } = string.Empty;
    public string EntityId { get; private set; } = string.Empty;
    public string? ClientId { get; private set; }
    public string? MetadataJson { get; private set; }
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;

    public static AuditLog Create(
        string id,
        string? actorUserId,
        string actorRole,
        string action,
        string entityType,
        string entityId,
        string? clientId,
        string? metadataJson,
        DateTime? createdAtUtc = null)
    {
        return new AuditLog
        {
            Id = id,
            ActorUserId = string.IsNullOrWhiteSpace(actorUserId) ? null : actorUserId.Trim(),
            ActorRole = string.IsNullOrWhiteSpace(actorRole) ? "unknown" : actorRole.Trim().ToLowerInvariant(),
            Action = string.IsNullOrWhiteSpace(action) ? throw new ArgumentException("Action is required.", nameof(action)) : action.Trim(),
            EntityType = string.IsNullOrWhiteSpace(entityType) ? throw new ArgumentException("Entity type is required.", nameof(entityType)) : entityType.Trim(),
            EntityId = string.IsNullOrWhiteSpace(entityId) ? throw new ArgumentException("Entity id is required.", nameof(entityId)) : entityId.Trim(),
            ClientId = string.IsNullOrWhiteSpace(clientId) ? null : clientId.Trim(),
            MetadataJson = string.IsNullOrWhiteSpace(metadataJson) ? null : metadataJson,
            CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow
        };
    }
}
