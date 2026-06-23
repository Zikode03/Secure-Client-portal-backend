namespace SecureClientPortal.Backend.Models;

public class DocumentAccessLog
{
    public string Id { get; private set; } = string.Empty;
    public string DocumentId { get; private set; } = string.Empty;
    public string ClientId { get; private set; } = string.Empty;
    public string? AccessedByUserId { get; private set; }
    public string AccessedByRole { get; private set; } = "unknown";
    public string Action { get; private set; } = string.Empty;
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }
    public string? MetadataJson { get; private set; }
    public DateTime AccessedAtUtc { get; private set; } = DateTime.UtcNow;

    public static DocumentAccessLog Create(
        string id,
        string documentId,
        string clientId,
        string? accessedByUserId,
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
            DocumentId = string.IsNullOrWhiteSpace(documentId) ? throw new ArgumentException("Document id is required.", nameof(documentId)) : documentId.Trim(),
            ClientId = string.IsNullOrWhiteSpace(clientId) ? throw new ArgumentException("Client id is required.", nameof(clientId)) : clientId.Trim(),
            AccessedByUserId = string.IsNullOrWhiteSpace(accessedByUserId) ? null : accessedByUserId.Trim(),
            AccessedByRole = string.IsNullOrWhiteSpace(accessedByRole) ? "unknown" : accessedByRole.Trim().ToLowerInvariant(),
            Action = string.IsNullOrWhiteSpace(action) ? throw new ArgumentException("Action is required.", nameof(action)) : action.Trim(),
            IpAddress = string.IsNullOrWhiteSpace(ipAddress) ? null : ipAddress.Trim(),
            UserAgent = string.IsNullOrWhiteSpace(userAgent) ? null : userAgent.Trim(),
            MetadataJson = string.IsNullOrWhiteSpace(metadataJson) ? null : metadataJson,
            AccessedAtUtc = accessedAtUtc ?? DateTime.UtcNow
        };
    }
}
