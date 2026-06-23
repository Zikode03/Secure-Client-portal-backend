namespace SecureClientPortal.Backend.Models;

public class TaskItem
{
    public Guid Id { get; private set; }
    public Guid ClientId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Status { get; private set; } = "todo";
    public string Priority { get; private set; } = "medium";
    public DateTime? DueDateUtc { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; private set; } = DateTime.UtcNow;

    public static TaskItem Create(
        Guid id,
        Guid clientId,
        string title,
        string status,
        string priority,
        DateTime? dueDateUtc,
        Guid createdByUserId,
        DateTime? createdAtUtc = null)
    {
        var item = new TaskItem
        {
            Id = id,
            ClientId = clientId == Guid.Empty ? throw new ArgumentException("Client id is required.", nameof(clientId)) : clientId,
            CreatedByUserId = createdByUserId == Guid.Empty ? throw new ArgumentException("Created by user id is required.", nameof(createdByUserId)) : createdByUserId,
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






