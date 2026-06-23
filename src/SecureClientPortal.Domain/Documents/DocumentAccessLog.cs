namespace SecureClientPortal.Backend.Models;

public class DocumentAccessLog
{
    public Guid Id { get; private set; }
    public Guid DocumentId { get; private set; }
    public Guid ClientId { get; private set; }
    public Guid? AccessedByUserId { get; private set; }
    public string AccessedByRole { get; private set; } = "unknown";
    public string Action { get; private set; } = string.Empty;
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }
    public string? MetadataJson { get; private set; }
    public DateTime AccessedAtUtc { get; private set; } = DateTime.UtcNow;

    public static DocumentAccessLog Create(
        Guid id,
        Guid documentId,
        Guid clientId,
        Guid? accessedByUserId,
        string accessedByRole,
        string action,
        string? ipAddress,
        string? userAgent,
        string? metadataJson,
        DateTime? accessedAtUtc = null)
    {
        return new DocumentAccessLog
        {
            Id = id,
            DocumentId = documentId == Guid.Empty ? throw new ArgumentException("Document id is required.", nameof(documentId)) : documentId,
            ClientId = clientId == Guid.Empty ? throw new ArgumentException("Client id is required.", nameof(clientId)) : clientId,
            AccessedByUserId = accessedByUserId == Guid.Empty ? null : accessedByUserId,
            AccessedByRole = string.IsNullOrWhiteSpace(accessedByRole) ? "unknown" : accessedByRole.Trim().ToLowerInvariant(),
            Action = string.IsNullOrWhiteSpace(action) ? throw new ArgumentException("Action is required.", nameof(action)) : action.Trim(),
            IpAddress = string.IsNullOrWhiteSpace(ipAddress) ? null : ipAddress.Trim(),
            UserAgent = string.IsNullOrWhiteSpace(userAgent) ? null : userAgent.Trim(),
            MetadataJson = string.IsNullOrWhiteSpace(metadataJson) ? null : metadataJson,
            AccessedAtUtc = accessedAtUtc ?? DateTime.UtcNow
        };
    }
}






