using SecureClientPortal.Backend.Application.Common.Events;
using SecureClientPortal.Backend.Application.Notifications.Events;

namespace SecureClientPortal.Backend.Infrastructure.Documents.Application.Events;

public sealed class DocumentReviewedDomainEventHandler : IDomainEventHandler<DocumentReviewedDomainEvent>
{
    public Task<IReadOnlyCollection<IIntegrationEvent>> HandleAsync(DocumentReviewedDomainEvent domainEvent, CancellationToken ct = default)
    {
        if (domainEvent.Decision == "under_review")
        {
            return Task.FromResult<IReadOnlyCollection<IIntegrationEvent>>([]);
        }

        var type = domainEvent.Decision switch
        {
            "accepted" => "document.approved",
            "rejected" => "document.rejected",
            "request_reupload" => "document.reupload_requested",
            _ => "document.reviewed"
        };

        var title = domainEvent.Decision switch
        {
            "accepted" => "Document approved",
            "rejected" => "Document rejected",
            "request_reupload" => "Re-upload requested",
            _ => "Document reviewed"
        };

        var message = domainEvent.Decision switch
        {
            "accepted" => $"Document '{domainEvent.DocumentName}' was approved.",
            "rejected" => $"Document '{domainEvent.DocumentName}' was rejected.",
            "request_reupload" => $"A corrected version of '{domainEvent.DocumentName}' was requested.",
            _ => $"Document '{domainEvent.DocumentName}' was reviewed."
        };

        var integrationEvent = new NotificationRequestedIntegrationEvent(
            domainEvent.ReviewerUserId,
            domainEvent.ReviewerRole,
            domainEvent.ClientId,
            "client",
            type,
            title,
            message,
            $"/documents/{domainEvent.DocumentId}",
            new { documentId = domainEvent.DocumentId, reason = domainEvent.Reason, decision = domainEvent.Decision },
            domainEvent.OccurredAtUtc);

        return Task.FromResult<IReadOnlyCollection<IIntegrationEvent>>([integrationEvent]);
    }
}
