namespace SecureClientPortal.Backend.Models;

public class MonthlyPackTemplateItem
{
    public string Id { get; private set; } = string.Empty;
    public string MonthlyPackTemplateId { get; private set; } = string.Empty;
    public string RequiredDocumentTemplateId { get; private set; } = string.Empty;
    public int SortOrder { get; private set; }
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;

    public static MonthlyPackTemplateItem Create(
        string id,
        string monthlyPackTemplateId,
        string requiredDocumentTemplateId,
        int sortOrder,
        DateTime? createdAtUtc = null)
    {
        return new MonthlyPackTemplateItem
        {
            Id = id,
            MonthlyPackTemplateId = string.IsNullOrWhiteSpace(monthlyPackTemplateId) ? throw new ArgumentException("Monthly pack template id is required.", nameof(monthlyPackTemplateId)) : monthlyPackTemplateId.Trim(),
            RequiredDocumentTemplateId = string.IsNullOrWhiteSpace(requiredDocumentTemplateId) ? throw new ArgumentException("Required document template id is required.", nameof(requiredDocumentTemplateId)) : requiredDocumentTemplateId.Trim(),
            SortOrder = sortOrder,
            CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow
        };
    }

    public void UpdateSortOrder(int sortOrder)
    {
        SortOrder = sortOrder;
    }
}
