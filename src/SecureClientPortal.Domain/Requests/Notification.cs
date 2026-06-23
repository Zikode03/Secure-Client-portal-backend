namespace SecureClientPortal.Backend.Models;

public class Notification
{
    public Guid Id { get; set; }
    public Guid UserId { get; private set; }
    public Guid? ClientId { get; private set; }
    public string Type { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string Message { get; private set; } = string.Empty;
    public string? LinkUrl { get; private set; }
    public bool IsRead { get; private set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ReadAtUtc { get; private set; }

    public static Notification Create(Guid id, Guid userId, Guid? clientId, string type, string title, string message, string? linkUrl, DateTime? createdAtUtc = null)
    {
        var item = new Notification
        {
            Id = id,
            CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow
        };

        item.UserId = userId;
        item.ClientId = clientId == Guid.Empty ? null : clientId;
        item.Type = type.Trim();
        item.Title = title.Trim();
        item.Message = message.Trim();
        item.LinkUrl = string.IsNullOrWhiteSpace(linkUrl) ? null : linkUrl.Trim();
        item.IsRead = false;
        item.ReadAtUtc = null;

        return item;
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






