namespace SecureClientPortal.Backend.Models;

public class RequestItem
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public string RequestType { get; private set; } = "clarification_needed";
    public Guid? RelatedDocumentId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string Priority { get; private set; } = RequestPriority.Medium.ToStorageValue();
    public string Status { get; private set; } = RequestStatus.Open.ToStorageValue();
    public DateTime? DueDateUtc { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public Guid? ResolvedByUserId { get; private set; }
    public DateTime RequestedAtUtc { get; private set; } = DateTime.UtcNow;
    public DateTime? ResolvedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; } = DateTime.UtcNow;

    public static RequestItem Create(Guid id, Guid clientId, string requestType, Guid? relatedDocumentId, string title, string description, RequestPriority priority, Guid requestedByUserId, RequestStatus initialStatus, DateTime? dueDateUtc)
    {
        var item = new RequestItem
        {
            Id = id,
            ClientId = clientId,
            RequestedByUserId = requestedByUserId,
            RequestedAtUtc = DateTime.UtcNow
        };
        item.UpdateDetails(requestType, relatedDocumentId, title, description, priority, dueDateUtc);
        item.SetStatus(initialStatus);
        item.UpdatedAtUtc = item.RequestedAtUtc;
        return item;
    }

    public void UpdateDetails(string requestType, Guid? relatedDocumentId, string title, string description, RequestPriority priority, DateTime? dueDateUtc)
    {
        RequestType = RequestDomainValues.NormalizeRequestType(requestType);
        RelatedDocumentId = relatedDocumentId == Guid.Empty ? null : relatedDocumentId;
        Title = title.Trim();
        Description = description.Trim();
        Priority = priority.ToStorageValue();
        DueDateUtc = dueDateUtc;
        Touch();
    }

    public void SetStatus(RequestStatus status)
    {
        Status = status.ToStorageValue();
        if (status != RequestStatus.Resolved)
        {
            ResolvedByUserId = null;
            ResolvedAtUtc = null;
        }
        Touch();
    }

    public void MarkWaitingOnClient() => SetStatus(RequestStatus.WaitingOnClient);
    public void MarkWaitingOnAccountant() => SetStatus(RequestStatus.WaitingOnAccountant);
    public void MarkOverdue() => SetStatus(RequestStatus.Overdue);

    public void Resolve(Guid resolvedByUserId)
    {
        Status = RequestStatus.Resolved.ToStorageValue();
        ResolvedByUserId = resolvedByUserId;
        ResolvedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = ResolvedAtUtc.Value;
    }

    private void Touch()
    {
        UpdatedAtUtc = DateTime.UtcNow;
    }
}






