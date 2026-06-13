namespace SecureClientPortal.Backend.Models;

public class MonthlyPackTemplateItem
{
    public string Id { get; set; } = string.Empty;
    public string MonthlyPackTemplateId { get; set; } = string.Empty;
    public string RequiredDocumentTemplateId { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
