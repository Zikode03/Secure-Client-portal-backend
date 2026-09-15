namespace SecureClientPortal.Backend.Models;

// Stable relational identity and evidence ownership; versioned workflow/rule snapshots
// are stored together so later rule edits cannot rewrite historical obligations.
public sealed class ComplianceObligation
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public Guid ComplianceItemId { get; set; }
    public string Code { get; set; } = "";
    public DateTime PeriodStartUtc { get; set; }
    public string RuleJson { get; set; } = "{}";
    public string StateJson { get; set; } = "{}";
}

public sealed class ComplianceAutomationConfiguration
{
    public string Key { get; set; } = "";
    public Guid? ClientId { get; set; }
    public string PayloadJson { get; set; } = "{}";
}

