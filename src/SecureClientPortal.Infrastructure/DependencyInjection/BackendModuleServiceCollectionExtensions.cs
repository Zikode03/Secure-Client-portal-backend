using Microsoft.Extensions.DependencyInjection;
using SecureClientPortal.Backend.Application;
using SecureClientPortal.Backend.Application.Common.Events;
using SecureClientPortal.Backend.Application.Modules.Assignments;
using SecureClientPortal.Backend.Application.Modules.AuditLogs;
using SecureClientPortal.Backend.Application.Modules.Auth;
using SecureClientPortal.Backend.Application.Modules.Clients;
using SecureClientPortal.Backend.Application.Modules.Compliance;
using SecureClientPortal.Backend.Application.Modules.Documents;
using SecureClientPortal.Backend.Application.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Application.Modules.Notifications;
using SecureClientPortal.Backend.Application.Modules.Notifications.Events;
using SecureClientPortal.Backend.Application.Modules.Requests;
using SecureClientPortal.Backend.Application.Modules.Reports;
using SecureClientPortal.Backend.Application.Modules.ReviewQueue;
using SecureClientPortal.Backend.Application.Modules.UsersRoles;
using SecureClientPortal.Backend.Application.Modules.FirmManagement;
using SecureClientPortal.Backend.Application.Modules.Platform;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Domain.Modules.Documents.Events;
using SecureClientPortal.Backend.Domain.Modules.Requests.Events;
using SecureClientPortal.Backend.Infrastructure.Common.Events;
using SecureClientPortal.Backend.Infrastructure.Modules.Assignments.Application;
using SecureClientPortal.Backend.Infrastructure.Modules.AuditLogs;
using SecureClientPortal.Backend.Infrastructure.Modules.Auth.Application;
using SecureClientPortal.Backend.Infrastructure.Modules.Clients;
using SecureClientPortal.Backend.Infrastructure.Modules.Compliance.Application;
using SecureClientPortal.Backend.Infrastructure.Modules.Documents.Application;
using SecureClientPortal.Backend.Infrastructure.Modules.Documents.Application.Events;
using SecureClientPortal.Backend.Infrastructure.Modules.Documents.Storage;
using SecureClientPortal.Backend.Infrastructure.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Infrastructure.Modules.Notifications;
using SecureClientPortal.Backend.Infrastructure.Modules.Notifications.Application;
using SecureClientPortal.Backend.Infrastructure.Modules.Requests;
using SecureClientPortal.Backend.Infrastructure.Modules.Requests.Application;
using SecureClientPortal.Backend.Infrastructure.Modules.Requests.Application.Events;
using SecureClientPortal.Backend.Infrastructure.Modules.Reports;
using SecureClientPortal.Backend.Infrastructure.Modules.ReviewQueue;
using SecureClientPortal.Backend.Infrastructure.Modules.FirmManagement.Application;
using SecureClientPortal.Backend.Infrastructure.Modules.UsersRoles.Application;
using SecureClientPortal.Backend.Infrastructure.Modules.Platform;

namespace SecureClientPortal.Backend.Infrastructure.DependencyInjection;

public static class BackendModuleServiceCollectionExtensions
{
    public static IServiceCollection AddPlatformModule(this IServiceCollection services)
    {
        services.AddSingleton<ICurrentUserContextFactory, CurrentUserContextFactory>();
        services.AddScoped<IHealthService, HealthService>();
        // Production automation is wrapped so automatic month creation respects each client's
        // selected monthly-pack profile instead of applying every active firm template to everyone.
        services.AddScoped<IAutomationWorkflowService, ProfileAwareAutomationWorkflowService>();
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        services.AddScoped<IIntegrationEventDispatcher, IntegrationEventDispatcher>();
        services.AddHostedService<AutomationBackgroundService>();
        return services;
    }

    public static IServiceCollection AddAuthModule(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();
        return services;
    }

    public static IServiceCollection AddUsersRolesModule(this IServiceCollection services)
    {
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IRoleService, RoleService>();
        services.AddScoped<IAdminService, AdminService>();
        return services;
    }

    public static IServiceCollection AddDocumentModule(this IServiceCollection services)
    {
        services.AddScoped<IFileStorage, LocalFileStorage>();
        services.AddScoped<IDocumentModuleDbContext>(sp => sp.GetRequiredService<PortalDbContext>());
        services.AddScoped<IDocumentQueryService, DocumentQueryService>();
        services.AddScoped<IDocumentCommandService, DocumentCommandService>();
        services.AddScoped<IDocumentLifecycleService, DocumentLifecycleService>();
        services.AddScoped<IDocumentWorkflowService, DocumentWorkflowService>();
        services.AddScoped<IDomainEventHandler<DocumentReviewedDomainEvent>, DocumentReviewedDomainEventHandler>();
        return services;
    }

    public static IServiceCollection AddNotificationsModule(this IServiceCollection services)
    {
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IDomainEventHandler<RequestCreatedDomainEvent>, RequestCreatedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<RequestResolvedDomainEvent>, RequestResolvedDomainEventHandler>();
        services.AddScoped<IIntegrationEventHandler<NotificationRequestedIntegrationEvent>, NotificationRequestedIntegrationEventHandler>();
        return services;
    }

    public static IServiceCollection AddMonthlyPacksModule(this IServiceCollection services)
    {
        // Monthly packs have two layers: the pack workflow itself and a client-specific profile
        // that determines which recurring slots should exist for each client.
        services.AddScoped<IDocumentSlotService, DocumentSlotService>();
        services.AddScoped<IClientMonthlyPackProfileService, ClientMonthlyPackProfileService>();
        services.AddScoped<IMonthlyPackService, MonthlyPackService>();
        return services;
    }

    public static IServiceCollection AddClientsModule(this IServiceCollection services)
    {
        services.AddScoped<IClientService, ClientService>();
        return services;
    }

    public static IServiceCollection AddAssignmentsModule(this IServiceCollection services)
    {
        services.AddScoped<IAssignmentService, AssignmentService>();
        return services;
    }

    public static IServiceCollection AddFirmManagementModule(this IServiceCollection services)
    {
        services.AddScoped<IFirmManagementService, FirmManagementService>();
        return services;
    }

    public static IServiceCollection AddRequestModule(this IServiceCollection services)
    {
        services.AddScoped<IRequestModuleDbContext>(sp => sp.GetRequiredService<PortalDbContext>());
        services.AddScoped<IRequestQueryService, RequestQueryService>();
        services.AddScoped<IRequestCommandService, RequestCommandService>();
        services.AddScoped<IRequestService, RequestService>();
        services.AddScoped<ITaskService, TaskService>();
        return services;
    }

    public static IServiceCollection AddReviewQueueModule(this IServiceCollection services)
    {
        services.AddScoped<IReviewQueueService, ReviewQueueService>();
        return services;
    }

    public static IServiceCollection AddComplianceModule(this IServiceCollection services)
    {
        services.AddScoped<IComplianceService, ComplianceService>();
        return services;
    }

    public static IServiceCollection AddAuditLogsModule(this IServiceCollection services)
    {
        services.AddScoped<IAuditLogService, AuditLogService>();
        return services;
    }

    public static IServiceCollection AddReportsModule(this IServiceCollection services)
    {
        services.AddScoped<IReportService, ReportService>();
        return services;
    }
}
