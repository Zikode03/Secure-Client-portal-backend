namespace SecureClientPortal.Backend.Models;

public class RequestComment
{
    public Guid Id { get; private set; }
    public Guid RequestId { get; private set; }
    public Guid ClientId { get; private set; }
    public Guid AuthorUserId { get; private set; }
    public string AuthorRole { get; private set; } = string.Empty;
    public string Message { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;

    public static RequestComment Create(
        Guid id,
        Guid requestId,
        Guid clientId,
        Guid authorUserId,
        string authorRole,
        string message,
        DateTime? createdAtUtc = null)
    {
        return new RequestComment
        {
            Id = id,
            RequestId = requestId == Guid.Empty ? throw new ArgumentException("Request id is required.", nameof(requestId)) : requestId,
            ClientId = clientId == Guid.Empty ? throw new ArgumentException("Client id is required.", nameof(clientId)) : clientId,
            AuthorUserId = authorUserId == Guid.Empty ? throw new ArgumentException("Author user id is required.", nameof(authorUserId)) : authorUserId,
            AuthorRole = string.IsNullOrWhiteSpace(authorRole) ? "unknown" : authorRole.Trim().ToLowerInvariant(),
            Message = string.IsNullOrWhiteSpace(message) ? throw new ArgumentException("Comment message is required.", nameof(message)) : message.Trim(),
            CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow
        };
    }
}






