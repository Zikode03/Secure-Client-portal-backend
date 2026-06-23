namespace SecureClientPortal.Backend.Models;

public class RequestTemplate
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string RequestType { get; private set; } = string.Empty;
    public string TitleTemplate { get; private set; } = string.Empty;
    public string DescriptionTemplate { get; private set; } = string.Empty;
    public string Priority { get; private set; } = "medium";
    public int? DefaultDueInDays { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; private set; } = DateTime.UtcNow;

    public static RequestTemplate Create(
        Guid id,
        string name,
        string requestType,
        string titleTemplate,
        string descriptionTemplate,
        string priority,
        int? defaultDueInDays,
        bool isActive = true,
        DateTime? createdAtUtc = null)
    {
        var item = new RequestTemplate
        {
            Id = id,
            CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow
        };

        item.Update(name, requestType, titleTemplate, descriptionTemplate, priority, defaultDueInDays, isActive);
        return item;
    }

    public void Update(
        string name,
        string requestType,
        string titleTemplate,
        string descriptionTemplate,
        string priority,
        int? defaultDueInDays,
        bool isActive)
    {
        if (defaultDueInDays < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(defaultDueInDays));
        }

        Name = string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("Template name is required.", nameof(name)) : name.Trim();
        RequestType = string.IsNullOrWhiteSpace(requestType) ? throw new ArgumentException("Request type is required.", nameof(requestType)) : requestType.Trim();
        TitleTemplate = string.IsNullOrWhiteSpace(titleTemplate) ? throw new ArgumentException("Title template is required.", nameof(titleTemplate)) : titleTemplate.Trim();
        DescriptionTemplate = string.IsNullOrWhiteSpace(descriptionTemplate) ? throw new ArgumentException("Description template is required.", nameof(descriptionTemplate)) : descriptionTemplate.Trim();
        Priority = string.IsNullOrWhiteSpace(priority) ? throw new ArgumentException("Priority is required.", nameof(priority)) : priority.Trim().ToLowerInvariant();
        DefaultDueInDays = defaultDueInDays;
        IsActive = isActive;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}






