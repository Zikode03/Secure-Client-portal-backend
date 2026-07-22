namespace SecureClientPortal.Backend.Infrastructure.Modules.Platform;

public sealed class AutomationOptions
{
    public const string Section = "Automation";

    public bool Enabled { get; set; } = true;
    public bool RunOnStartup { get; set; } = true;
    public int IntervalMinutes { get; set; } = 60;
}
