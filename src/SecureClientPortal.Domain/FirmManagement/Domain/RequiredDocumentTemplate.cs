namespace SecureClientPortal.Backend.Models;

public class RequiredDocumentTemplate
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string DocumentCategory { get; set; } = string.Empty;
    public bool IsRequired { get; set; } = true;
    public int? DefaultDueDayOfMonth { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
