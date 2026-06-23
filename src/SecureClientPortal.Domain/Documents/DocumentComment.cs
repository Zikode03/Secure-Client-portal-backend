namespace SecureClientPortal.Backend.Models;

public class DocumentComment
{
    public string Id { get; private set; } = string.Empty;
    public string DocumentId { get; private set; } = string.Empty;
    public string AuthorUserId { get; private set; } = string.Empty;
    public string AuthorRole { get; private set; } = string.Empty;
    public string Message { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;

    public static DocumentComment Create(
        string id,
        string documentId,
        string authorUserId,
        string authorRole,
        string message,
        DateTime? createdAtUtc = null)
    {
        return new DocumentComment
        {
            Id = id,
            DocumentId = string.IsNullOrWhiteSpace(documentId) ? throw new ArgumentException("Document id is required.", nameof(documentId)) : documentId.Trim(),
            AuthorUserId = string.IsNullOrWhiteSpace(authorUserId) ? throw new ArgumentException("Author user id is required.", nameof(authorUserId)) : authorUserId.Trim(),
            AuthorRole = string.IsNullOrWhiteSpace(authorRole) ? "unknown" : authorRole.Trim().ToLowerInvariant(),
            Message = string.IsNullOrWhiteSpace(message) ? throw new ArgumentException("Comment message is required.", nameof(message)) : message.Trim(),
            CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow
        };
    }
}
