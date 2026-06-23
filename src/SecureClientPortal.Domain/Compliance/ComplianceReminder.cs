namespace SecureClientPortal.Backend.Models;

public class ComplianceReminder
{
    public string Id { get; set; } = string.Empty;
    public string ComplianceItemId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string RecipientUserId { get; set; } = string.Empty;
    public string Type { get; private set; } = string.Empty;
    public string Status { get; private set; } = ComplianceReminderStatus.Pending.ToStorageValue();
    public DateTime ScheduledForUtc { get; private set; }
    public DateTime? SentAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public static ComplianceReminder Create(string id, string complianceItemId, string clientId, string recipientUserId, string type, DateTime scheduledForUtc)
    {
        return new ComplianceReminder
        {
            Id = id,
            ComplianceItemId = complianceItemId,
            ClientId = clientId,
            RecipientUserId = recipientUserId,
            Type = type.Trim().ToLowerInvariant(),
            Status = ComplianceReminderStatus.Pending.ToStorageValue(),
            ScheduledForUtc = scheduledForUtc,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    public void SetStatus(ComplianceReminderStatus status)
    {
        Status = status.ToStorageValue();
        if (status == ComplianceReminderStatus.Sent)
        {
            SentAtUtc = DateTime.UtcNow;
        }
    }
}
