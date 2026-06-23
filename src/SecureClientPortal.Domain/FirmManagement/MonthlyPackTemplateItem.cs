namespace SecureClientPortal.Backend.Models;

public class MonthlyPackTemplateItem
{
    public Guid Id { get; private set; }
    public Guid MonthlyPackTemplateId { get; private set; }
    public Guid RequiredDocumentTemplateId { get; private set; }
    public int SortOrder { get; private set; }
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;

    public static MonthlyPackTemplateItem Create(
        Guid id,
        Guid monthlyPackTemplateId,
        Guid requiredDocumentTemplateId,
        int sortOrder,
        DateTime? createdAtUtc = null)
    {
        return new MonthlyPackTemplateItem
        {
            Id = id,
            MonthlyPackTemplateId = monthlyPackTemplateId == Guid.Empty ? throw new ArgumentException("Monthly pack template id is required.", nameof(monthlyPackTemplateId)) : monthlyPackTemplateId,
            RequiredDocumentTemplateId = requiredDocumentTemplateId == Guid.Empty ? throw new ArgumentException("Required document template id is required.", nameof(requiredDocumentTemplateId)) : requiredDocumentTemplateId,
            SortOrder = sortOrder,
            CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow
        };
    }

    public void UpdateSortOrder(int sortOrder)
    {
        SortOrder = sortOrder;
    }
}





