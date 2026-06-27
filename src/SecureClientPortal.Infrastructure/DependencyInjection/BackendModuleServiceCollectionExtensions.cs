using Microsoft.Extensions.DependencyInjection;
using SecureClientPortal.Backend.Application;
using SecureClientPortal.Backend.Application.Common.Events;
using SecureClientPortal.Backend.Application.Compliance;
using SecureClientPortal.Backend.Application.Documents;
using SecureClientPortal.Backend.Application.FirmManagement;
using SecureClientPortal.Backend.Application.Identity;
using SecureClientPortal.Backend.Application.Notifications.Events;
using SecureClientPortal.Backend.Application.Platform;
using SecureClientPortal.Backend.Application.Reporting;
using SecureClientPortal.Backend.Application.Requests;
using SecureClientPortal.Backend.Application.Roles;
using SecureClientPortal.Backend.Application.Assignments;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Infrastructure.Common.Events;
using SecureClientPortal.Backend.Infrastructure.Compliance.Application;
using SecureClientPortal.Backend.Infrastructure.Documents;
using SecureClientPortal.Backend.Infrastructure.Documents.Application;
using SecureClientPortal.Backend.Infrastructure.Documents.Application.Events;
using SecureClientPortal.Backend.Infrastructure.FirmManagement;
using SecureClientPortal.Backend.Infrastructure.FirmManagement.Application;
using SecureClientPortal.Backend.Infrastructure.Identity.Application;
using SecureClientPortal.Backend.Infrastructure.Notifications.Application;
using SecureClientPortal.Backend.Infrastructure.Platform;
using SecureClientPortal.Backend.Infrastructure.Reporting;
using SecureClientPortal.Backend.Infrastructure.Requests.Application;
using SecureClientPortal.Backend.Infrastructure.Requests.Application.Events;
using SecureClientPortal.Backend.Storage;
using StorageIFileStorage = SecureClientPortal.Backend.Storage.IFileStorage;

namespace SecureClientPortal.Backend.Infrastructure.DependencyInjection;

public static class BackendModuleServiceCollectionExtensions
{
    public static IServiceCollection AddPlatformModule(this IServiceCollection services)
    {
        services.AddSingleton<ICurrentUserContextFactory, CurrentUserContextFactory>();
        services.AddScoped<IAuditLogService, AuditLogService>();
        services.AddScoped<IHealthService, HealthService>();
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        services.AddScoped<IIntegrationEventDispatcher, IntegrationEventDispatcher>();
        return services;
    }

    public static IServiceCollection AddIdentityModule(this IServiceCollection services)
    {
        services.AddScoped<StorageIFileStorage, LocalFileStorage>();
        services.AddScoped<IRoleService, RoleService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IAdminService, AdminService>();
        return services;
    }

    public static IServiceCollection AddDocumentModule(this IServiceCollection services)
    {
        services.AddScoped<IDocumentModuleDbContext>(sp => sp.GetRequiredService<PortalDbContext>());
        services.AddScoped<IDocumentQueryService, DocumentQueryService>();
        services.AddScoped<IDocumentCommandService, DocumentCommandService>();
        services.AddScoped<IDocumentLifecycleService, DocumentLifecycleService>();
        services.AddScoped<IDocumentWorkflowService, DocumentWorkflowService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IDocumentSlotService, DocumentSlotService>();
        services.AddScoped<IMonthlyPackService, MonthlyPackService>();
        services.AddScoped<IDomainEventHandler<DocumentReviewedDomainEvent>, DocumentReviewedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<RequestCreatedDomainEvent>, RequestCreatedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<RequestResolvedDomainEvent>, RequestResolvedDomainEventHandler>();
        services.AddScoped<IIntegrationEventHandler<NotificationRequestedIntegrationEvent>, NotificationRequestedIntegrationEventHandler>();
        return services;
    }

    public static IServiceCollection AddFirmManagementModule(this IServiceCollection services)
    {
        services.AddScoped<IFirmManagementService, FirmManagementService>();
        services.AddScoped<IAssignmentService, AssignmentService>();
        services.AddScoped<IClientService, ClientService>();
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

    public static IServiceCollection AddComplianceModule(this IServiceCollection services)
    {
        services.AddScoped<IComplianceService, ComplianceService>();
        return services;
    }

    public static IServiceCollection AddReportingModule(this IServiceCollection services)
    {
        services.AddScoped<IReportService, ReportService>();
        return services;
    }
}
