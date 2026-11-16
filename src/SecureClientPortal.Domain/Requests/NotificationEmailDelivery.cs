namespace SecureClientPortal.Backend.Models;

// One durable email job per notification; historical notifications are never backfilled.
public sealed class NotificationEmailDelivery
{
    public Guid NotificationId { get; set; }
    public string Status { get; set; } = "pending";
    public int Attempts { get; set; }
    public DateTime NextAttemptAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? SentAtUtc { get; set; }
    public string? FailureCode { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
}
