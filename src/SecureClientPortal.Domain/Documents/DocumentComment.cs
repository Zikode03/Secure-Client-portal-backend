namespace SecureClientPortal.Backend.Models;

public class DocumentComment
{
    public Guid Id { get; private set; }
    public Guid DocumentId { get; private set; }
    public Guid AuthorUserId { get; private set; }
    public string AuthorRole { get; private set; } = string.Empty;
    public string Message { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;

    public static DocumentComment Create(
        Guid id,
        Guid documentId,
        Guid authorUserId,
        string authorRole,
        string message,
        DateTime? createdAtUtc = null)
    {
        return new DocumentComment
        {
            Id = id,
            DocumentId = documentId == Guid.Empty ? throw new ArgumentException("Document id is required.", nameof(documentId)) : documentId,
            AuthorUserId = authorUserId == Guid.Empty ? throw new ArgumentException("Author user id is required.", nameof(authorUserId)) : authorUserId,
            AuthorRole = string.IsNullOrWhiteSpace(authorRole) ? "unknown" : authorRole.Trim().ToLowerInvariant(),
            Message = string.IsNullOrWhiteSpace(message) ? throw new ArgumentException("Comment message is required.", nameof(message)) : message.Trim(),
            CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow
        };
    }
}






