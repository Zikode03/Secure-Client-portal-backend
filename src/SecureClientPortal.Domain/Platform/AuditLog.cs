namespace SecureClientPortal.Backend.Models;

public class AuditLog
{
    public Guid Id { get; private set; }
    public Guid? ActorUserId { get; private set; }
    public string ActorRole { get; private set; } = "unknown";
    public string Action { get; private set; } = string.Empty;
    public string EntityType { get; private set; } = string.Empty;
    public Guid EntityId { get; private set; }
    public Guid? ClientId { get; private set; }
    public string? MetadataJson { get; private set; }
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;

    public static AuditLog Create(
        Guid id,
        Guid? actorUserId,
        string actorRole,
        string action,
        string entityType,
        Guid entityId,
        Guid? clientId,
        string? metadataJson,
        DateTime? createdAtUtc = null)
    {
        return new AuditLog
        {
            Id = id,
            ActorUserId = actorUserId == Guid.Empty ? null : actorUserId,
            ActorRole = string.IsNullOrWhiteSpace(actorRole) ? "unknown" : actorRole.Trim().ToLowerInvariant(),
            Action = string.IsNullOrWhiteSpace(action) ? throw new ArgumentException("Action is required.", nameof(action)) : action.Trim(),
            EntityType = string.IsNullOrWhiteSpace(entityType) ? throw new ArgumentException("Entity type is required.", nameof(entityType)) : entityType.Trim(),
            EntityId = entityId == Guid.Empty ? throw new ArgumentException("Entity id is required.", nameof(entityId)) : entityId,
            ClientId = clientId == Guid.Empty ? null : clientId,
            MetadataJson = string.IsNullOrWhiteSpace(metadataJson) ? null : metadataJson,
            CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow
        };
    }
}






