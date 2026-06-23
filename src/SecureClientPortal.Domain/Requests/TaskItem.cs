namespace SecureClientPortal.Backend.Models;

public class TaskItem
{
    public string Id { get; private set; } = string.Empty;
    public string ClientId { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string Status { get; private set; } = "todo";
    public string Priority { get; private set; } = "medium";
    public DateTime? DueDateUtc { get; private set; }
    public string CreatedByUserId { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; private set; } = DateTime.UtcNow;

    public static TaskItem Create(
        string id,
        string clientId,
        string title,
        string status,
        string priority,
        DateTime? dueDateUtc,
        string createdByUserId,
        DateTime? createdAtUtc = null)
    {
        var item = new TaskItem
        {
            Id = id,
            ClientId = string.IsNullOrWhiteSpace(clientId) ? throw new ArgumentException("Client id is required.", nameof(clientId)) : clientId.Trim(),
            CreatedByUserId = string.IsNullOrWhiteSpace(createdByUserId) ? throw new ArgumentException("Created by user id is required.", nameof(createdByUserId)) : createdByUserId.Trim(),
            CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow
        };

        item.Update(title, status, priority, dueDateUtc);
        return item;
    }

    public void Update(string title, string status, string priority, DateTime? dueDateUtc)
    {
        Title = string.IsNullOrWhiteSpace(title) ? throw new ArgumentException("Task title is required.", nameof(title)) : title.Trim();
        Status = string.IsNullOrWhiteSpace(status) ? "todo" : status.Trim().ToLowerInvariant();
        Priority = string.IsNullOrWhiteSpace(priority) ? "medium" : priority.Trim().ToLowerInvariant();
        DueDateUtc = dueDateUtc;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
