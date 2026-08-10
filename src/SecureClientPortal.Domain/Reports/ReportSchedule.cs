using System.Text.Json;

namespace SecureClientPortal.Backend.Models;

public class ReportSchedule
{
    public Guid Id { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public Guid? ClientId { get; private set; }
    public string ReportType { get; private set; } = "compliance";
    public string Frequency { get; private set; } = "monthly";
    public string RecipientsJson { get; private set; } = "[]";
    public DateTime NextRunAtUtc { get; private set; }
    public DateTime LastScheduledAtUtc { get; private set; } = DateTime.UtcNow;
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; private set; } = DateTime.UtcNow;

    public static ReportSchedule Create(
        Guid id,
        Guid createdByUserId,
        Guid? clientId,
        string frequency,
        IReadOnlyCollection<string> recipients,
        DateTime utcNow)
    {
        if (id == Guid.Empty) throw new DomainRuleException("Report schedule id is required.");
        if (createdByUserId == Guid.Empty) throw new DomainRuleException("Creating user id is required.");

        var schedule = new ReportSchedule
        {
            Id = id,
            CreatedByUserId = createdByUserId,
            ClientId = clientId == Guid.Empty ? null : clientId,
            CreatedAtUtc = utcNow.ToUniversalTime()
        };
        schedule.Update(frequency, recipients, utcNow);
        return schedule;
    }

    public void Update(string frequency, IReadOnlyCollection<string> recipients, DateTime utcNow)
    {
        var normalizedFrequency = frequency?.Trim().ToLowerInvariant();
        if (normalizedFrequency is not ("weekly" or "monthly"))
        {
            throw new DomainRuleException("Frequency must be weekly or monthly.");
        }

        var normalizedRecipients = recipients
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => EmailAddress.Parse(x).Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToArray();
        if (normalizedRecipients.Length == 0)
        {
            throw new DomainRuleException("At least one report recipient is required.");
        }

        var now = utcNow.ToUniversalTime();
        Frequency = normalizedFrequency;
        RecipientsJson = JsonSerializer.Serialize(normalizedRecipients);
        LastScheduledAtUtc = now;
        NextRunAtUtc = CalculateNextRun(now, normalizedFrequency);
        UpdatedAtUtc = now;
    }

    public IReadOnlyCollection<string> GetRecipients()
    {
        try
        {
            return JsonSerializer.Deserialize<string[]>(RecipientsJson) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static DateTime CalculateNextRun(DateTime utcNow, string frequency)
    {
        if (frequency == "monthly")
        {
            return new DateTime(utcNow.Year, utcNow.Month, 1, 6, 0, 0, DateTimeKind.Utc).AddMonths(1);
        }

        var daysUntilMonday = ((int)DayOfWeek.Monday - (int)utcNow.DayOfWeek + 7) % 7;
        if (daysUntilMonday == 0 && utcNow.TimeOfDay >= TimeSpan.FromHours(6))
        {
            daysUntilMonday = 7;
        }

        return utcNow.Date.AddDays(daysUntilMonday).AddHours(6);
    }
}
