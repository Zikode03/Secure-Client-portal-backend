using SecureClientPortal.Backend.Application;
using SecureClientPortal.Backend.Application.Common.Events;
using SecureClientPortal.Backend.Application.Modules.Documents;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Infrastructure.Common.Events;
using SecureClientPortal.Backend.Infrastructure.Modules.Documents.Application;
using SecureClientPortal.Backend.Infrastructure.Modules.Documents.Application.Events;
using SecureClientPortal.Backend.Infrastructure.Modules.Notifications.Application;
using SecureClientPortal.Backend.Infrastructure.Modules.Requests.Application.Events;

namespace SecureClientPortal.Backend.Tests;

internal static class DocumentWorkflowTestFactory
{
    public static IDocumentWorkflowService Create(PortalDbContext db, IFileStorage fileStorage)
    {
        return new DocumentWorkflowService(
            new DocumentQueryService(db, db, fileStorage),
            new DocumentCommandService(db, db, fileStorage),
            new DocumentLifecycleService(db, db, new CurrentUserContextFactory(), CreateStandaloneDispatcher(db)));
    }

    private static IDomainEventDispatcher CreateStandaloneDispatcher(PortalDbContext db)
    {
        var integrationDispatcher = new StandaloneIntegrationEventDispatcher(
        [
            new NotificationRequestedIntegrationEventHandler(db)
        ]);

        return new StandaloneDomainEventDispatcher(
        [
            new DocumentReviewedDomainEventHandler(),
            new RequestCreatedDomainEventHandler(),
            new RequestResolvedDomainEventHandler()
        ], integrationDispatcher);
    }
}
