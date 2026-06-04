namespace SecureClientPortal.Backend.Models;

public class DocumentSlot
{
    public string Id { get; set; } = string.Empty;
    public string MonthlyPackId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool IsRequired { get; set; } = true;
    public string Status { get; set; } = "missing";
    public string? CurrentDocumentId { get; set; }
    public DateTime? DueDateUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
