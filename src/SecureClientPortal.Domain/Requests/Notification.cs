namespace SecureClientPortal.Backend.Models;

public class Notification
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid? ClientId { get; private set; }
    public string Type { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string Message { get; private set; } = string.Empty;
    public string? LinkUrl { get; private set; }
    public bool IsRead { get; private set; }
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;
    public DateTime? ReadAtUtc { get; private set; }

    public static Notification Create(Guid id, Guid userId, Guid? clientId, string type, string title, string message, string? linkUrl, DateTime? createdAtUtc = null)
    {
        if (id == Guid.Empty) throw new DomainRuleException("Notification id is required.");
        if (userId == Guid.Empty) throw new DomainRuleException("Notification user id is required.");
        if (string.IsNullOrWhiteSpace(type)) throw new DomainRuleException("Notification type is required.");
        if (string.IsNullOrWhiteSpace(title)) throw new DomainRuleException("Notification title is required.");
        if (string.IsNullOrWhiteSpace(message)) throw new DomainRuleException("Notification message is required.");

        return new Notification
        {
            Id = id,
            UserId = userId,
            ClientId = clientId == Guid.Empty ? null : clientId,
            Type = type.Trim(),
            Title = title.Trim(),
            Message = message.Trim(),
            LinkUrl = string.IsNullOrWhiteSpace(linkUrl) ? null : linkUrl.Trim(),
            IsRead = false,
            ReadAtUtc = null,
            CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow
        };
    }

    public void MarkRead()
    {
        if (IsRead)
        {
            return;
        }

        IsRead = true;
        ReadAtUtc = DateTime.UtcNow;
    }
}
