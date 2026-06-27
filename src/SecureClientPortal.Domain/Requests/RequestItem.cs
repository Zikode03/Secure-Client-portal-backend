using System.ComponentModel.DataAnnotations.Schema;

namespace SecureClientPortal.Backend.Models;

public class RequestItem : IHasDomainEvents
{
    [NotMapped]
    private readonly List<IDomainEvent> _domainEvents = [];

    public Guid Id { get; private set; }
    public Guid ClientId { get; private set; }
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

    public static RequestItem Create(Guid id, Guid clientId, string requestType, Guid? relatedDocumentId, string title, string description, RequestPriority priority, Guid requestedByUserId, RequestStatus initialStatus, DateTime? dueDateUtc, DateTime? requestedAtUtc = null)
    {
        if (id == Guid.Empty) throw new DomainRuleException("Request id is required.");
        if (clientId == Guid.Empty) throw new DomainRuleException("Client id is required.");
        if (requestedByUserId == Guid.Empty) throw new DomainRuleException("Requesting user id is required.");

        var createdAt = requestedAtUtc ?? DateTime.UtcNow;
        var item = new RequestItem
        {
            Id = id,
            ClientId = clientId,
            RequestedByUserId = requestedByUserId,
            RequestedAtUtc = createdAt,
            UpdatedAtUtc = createdAt
        };

        item.UpdateDetails(requestType, relatedDocumentId, title, description, priority, dueDateUtc);
        item.SetStatus(initialStatus);
        item.UpdatedAtUtc = item.RequestedAtUtc;
        return item;
    }

    public void RecordCreated(WorkflowActorContext actor, DateTime? occurredAtUtc = null)
    {
        _domainEvents.Add(new RequestCreatedDomainEvent(
            Id,
            ClientId,
            RelatedDocumentId,
            RequestType,
            Title,
            Priority,
            actor,
            occurredAtUtc ?? RequestedAtUtc));
    }

    public void UpdateDetails(string requestType, Guid? relatedDocumentId, string title, string description, RequestPriority priority, DateTime? dueDateUtc)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new DomainRuleException("Request title is required.");
        if (string.IsNullOrWhiteSpace(description)) throw new DomainRuleException("Request description is required.");

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
        if (resolvedByUserId == Guid.Empty) throw new DomainRuleException("Resolving user id is required.");

        Status = RequestStatus.Resolved.ToStorageValue();
        ResolvedByUserId = resolvedByUserId;
        ResolvedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = ResolvedAtUtc.Value;
        _domainEvents.Add(new RequestResolvedDomainEvent(
            Id,
            ClientId,
            Title,
            RequestType,
            resolvedByUserId,
            ResolvedAtUtc.Value));
    }

    public bool ShouldBeMarkedOverdue(DateTime now)
    {
        return Status != RequestStatus.Resolved.ToStorageValue()
            && DueDateUtc.HasValue
            && DueDateUtc.Value < now
            && Status != RequestStatus.Overdue.ToStorageValue();
    }

    private void Touch()
    {
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public IReadOnlyCollection<IDomainEvent> DequeueDomainEvents()
    {
        var events = _domainEvents.ToArray();
        _domainEvents.Clear();
        return events;
    }
}
