using SecureClientPortal.Backend.Models;
namespace SecureClientPortal.Backend.Tests;
public sealed class NotificationDeliveryPolicyTests
{
    [Fact]
    public void SavedPreferencesControlReminderAndEscalationDelivery()
    {
        var preferences = NotificationPreference.Create(Guid.NewGuid());
        preferences.Update(false, false, false, false, false, false, false, "22:00-06:00");
        var noon = new DateTime(2026, 11, 10, 10, 0, 0, DateTimeKind.Utc);
        Assert.False(NotificationDeliveryPolicy.Allows(preferences, "monthly_pack.reminder", noon));
        Assert.False(NotificationDeliveryPolicy.Allows(preferences, "request.overdue", noon));
        Assert.False(NotificationDeliveryPolicy.Allows(preferences, "document.rejected", noon));
        Assert.True(NotificationDeliveryPolicy.Allows(preferences, "security.reset", noon));
    }
    [Fact]
    public void QuietHoursUseSouthAfricaTimeAndPreserveOrdinaryActivity()
    {
        var preferences = NotificationPreference.Create(Guid.NewGuid());
        var night = new DateTime(2026, 11, 10, 21, 0, 0, DateTimeKind.Utc);
        Assert.False(NotificationDeliveryPolicy.Allows(preferences, "monthly_pack.reminder", night));
        Assert.True(NotificationDeliveryPolicy.Allows(preferences, "document.uploaded", night));
        Assert.True(NotificationDeliveryPolicy.Allows(preferences, "monthly_pack.reminder", night.AddHours(8)));
    }
}
