namespace SecureClientPortal.Backend.Models;

public class RequestComment
{
    public string Id { get; private set; } = string.Empty;
    public string RequestId { get; private set; } = string.Empty;
    public string ClientId { get; private set; } = string.Empty;
    public string AuthorUserId { get; private set; } = string.Empty;
    public string AuthorRole { get; private set; } = string.Empty;
    public string Message { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;

    public static RequestComment Create(
        string id,
        string requestId,
        string clientId,
        string authorUserId,
        string authorRole,
        string message,
        DateTime? createdAtUtc = null)
    {
        return new RequestComment
        {
            Id = id,
            RequestId = string.IsNullOrWhiteSpace(requestId) ? throw new ArgumentException("Request id is required.", nameof(requestId)) : requestId.Trim(),
            ClientId = string.IsNullOrWhiteSpace(clientId) ? throw new ArgumentException("Client id is required.", nameof(clientId)) : clientId.Trim(),
            AuthorUserId = string.IsNullOrWhiteSpace(authorUserId) ? throw new ArgumentException("Author user id is required.", nameof(authorUserId)) : authorUserId.Trim(),
            AuthorRole = string.IsNullOrWhiteSpace(authorRole) ? "unknown" : authorRole.Trim().ToLowerInvariant(),
            Message = string.IsNullOrWhiteSpace(message) ? throw new ArgumentException("Comment message is required.", nameof(message)) : message.Trim(),
            CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow
        };
    }
}
