namespace SecureClientPortal.Backend.Models;

public class SystemSetting
{
    public string Key { get; private set; } = string.Empty;
    public string ValueJson { get; private set; } = "{}";
    public DateTime UpdatedAtUtc { get; private set; } = DateTime.UtcNow;

    public static SystemSetting Create(string key, string valueJson, DateTime? updatedAtUtc = null)
    {
        return new SystemSetting
        {
            Key = string.IsNullOrWhiteSpace(key) ? throw new ArgumentException("Setting key is required.", nameof(key)) : key.Trim(),
            ValueJson = string.IsNullOrWhiteSpace(valueJson) ? "{}" : valueJson,
            UpdatedAtUtc = updatedAtUtc ?? DateTime.UtcNow
        };
    }

    public void UpdateValue(string valueJson)
    {
        ValueJson = string.IsNullOrWhiteSpace(valueJson) ? "{}" : valueJson;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
