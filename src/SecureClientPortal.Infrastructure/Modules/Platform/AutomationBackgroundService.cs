using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SecureClientPortal.Backend.Application.Modules.Platform;

namespace SecureClientPortal.Backend.Infrastructure.Modules.Platform;

public sealed class AutomationBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<AutomationOptions> _options;
    private readonly ILogger<AutomationBackgroundService> _logger;

    public AutomationBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<AutomationOptions> options,
        ILogger<AutomationBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = _options.Value;
        if (!options.Enabled)
        {
            _logger.LogInformation("Automation background service is disabled.");
            return;
        }

        if (options.RunOnStartup)
        {
            await RunOnceAsync(stoppingToken);
        }

        var intervalMinutes = Math.Max(5, options.IntervalMinutes);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(intervalMinutes));
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunOnceAsync(stoppingToken);
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IAutomationWorkflowService>();
            var result = await service.RunAsync(null, ct);
            _logger.LogInformation(
                "Automation run completed at {RunAtUtc}. Packs={Packs}, Slots={Slots}, DraftsAutoSubmitted={DraftsAutoSubmitted}, RequestEscalations={Escalations}, ComplianceReminders={ComplianceReminders}",
                result.RunAtUtc,
                result.MonthlyPacksCreated,
                result.DocumentSlotsCreated,
                result.DraftSlotsAutoSubmitted,
                result.RequestEscalationNotificationsSent,
                result.ComplianceReminderNotificationsSent);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Automation background run failed.");
        }
    }
}
