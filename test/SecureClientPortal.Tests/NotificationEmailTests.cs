using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SecureClientPortal.Backend.Application.Modules.Notifications;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Infrastructure.Modules.Notifications;
using SecureClientPortal.Backend.Models;
using Xunit;

namespace SecureClientPortal.Backend.Tests;
public sealed class NotificationEmailTests
{
    private static readonly DateTime Noon = DateTime.UtcNow.Date.AddHours(10);
    private sealed class Sender : INotificationEmailSender
    {
        public int Calls; public bool Fail;
        public Task SendAsync(string email, string name, string title, string path, Guid id, CancellationToken ct)
        { Calls++; if (Fail) throw new InvalidOperationException("test transport failure"); Assert.Equal("/client/notifications", path); return Task.CompletedTask; }
    }
    private static async Task<(PortalDbContext db, User user, Notification alert, Sender sender, NotificationEmailDispatcher dispatcher)> Setup()
    {
        var db = new PortalDbContext(new DbContextOptionsBuilder<PortalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var user = User.CreateInvited(Guid.NewGuid(), "Email test", "test@example.invalid", UserRole.Client, "hash", "[]", null);
        user.CompleteSetup("Email test", "hash");
        var alert = Notification.Create(Guid.NewGuid(), user.Id, null, "request.created", "New request", "Private document details", "/client/requests", Noon);
        db.Users.Add(user); db.Notifications.Add(alert); await db.SaveChangesAsync();
        db.NotificationEmailDeliveries.Local.Single().NextAttemptAtUtc = Noon.AddMinutes(-1); await db.SaveChangesAsync();
        var sender = new Sender();
        return (db, user, alert, sender, new NotificationEmailDispatcher(db, sender, NullLogger<NotificationEmailDispatcher>.Instance));
    }
    [Fact] public async Task New_notifications_queue_once_and_read_updates_do_not_queue_again()
    {
        var (db, _, alert, _, _) = await Setup(); using (db) {
        Assert.Single(await db.NotificationEmailDeliveries.ToListAsync());
        alert.MarkRead(); await db.SaveChangesAsync(); Assert.Single(await db.NotificationEmailDeliveries.ToListAsync()); }
    }
    [Fact] public async Task Sent_jobs_are_not_sent_again()
    {
        var (db, _, _, sender, dispatcher) = await Setup(); using (db) {
        Assert.Equal(1, await dispatcher.RunBatchAsync(Noon, default));
        Assert.Equal(0, await dispatcher.RunBatchAsync(Noon.AddHours(1), default));
        Assert.Equal(1, sender.Calls); Assert.Equal("sent", (await db.NotificationEmailDeliveries.SingleAsync()).Status); }
    }
    [Fact] public async Task Opted_out_recipients_keep_in_app_alert_but_receive_no_email()
    {
        var (db, user, _, sender, dispatcher) = await Setup(); using (db) {
        var preference = NotificationPreference.Create(user.Id); preference.Update(true,true,true,false,false,false,true,"22:00-06:00");
        db.NotificationPreferences.Add(preference); await db.SaveChangesAsync();
        await dispatcher.RunBatchAsync(Noon,default); Assert.Equal(0,sender.Calls); Assert.Single(await db.Notifications.ToListAsync());
        Assert.Equal("skipped",(await db.NotificationEmailDeliveries.SingleAsync()).Status); }
    }
    [Fact] public async Task Quiet_hours_defer_instead_of_discarding_email()
    {
        var (db, _, _, sender, dispatcher) = await Setup(); using (db) {
        await dispatcher.RunBatchAsync(Noon.Date.AddHours(21),default);
        Assert.Equal(0,sender.Calls); Assert.Equal("pending",(await db.NotificationEmailDeliveries.SingleAsync()).Status);
        await dispatcher.RunBatchAsync(Noon.AddDays(1),default); Assert.Equal(1,sender.Calls); }
    }
    [Fact] public async Task Transport_failure_is_retried_without_losing_the_in_app_alert()
    {
        var (db, _, _, sender, dispatcher) = await Setup(); using (db) {
        sender.Fail=true; await dispatcher.RunBatchAsync(Noon,default);
        var job=await db.NotificationEmailDeliveries.SingleAsync(); Assert.Equal("pending",job.Status); Assert.Equal(1,job.Attempts);
        Assert.Single(await db.Notifications.ToListAsync());
        sender.Fail=false; await dispatcher.RunBatchAsync(Noon.AddMinutes(3),default); Assert.Equal("sent",job.Status); Assert.Equal(2,sender.Calls); }
    }
    [Fact] public async Task Repeated_transport_failures_stop_after_five_attempts()
    {
        var (db, _, _, sender, dispatcher) = await Setup(); using (db) {
        sender.Fail=true; for(var i=0;i<5;i++) await dispatcher.RunBatchAsync(Noon.AddMinutes(i*40),default);
        var job=await db.NotificationEmailDeliveries.SingleAsync(); Assert.Equal("failed",job.Status); Assert.Equal(5,job.Attempts); }
    }
    [Fact] public async Task Read_notifications_do_not_generate_unnecessary_email()
    {
        var (db, _, alert, sender, dispatcher) = await Setup(); using (db) {
        alert.MarkRead(); await db.SaveChangesAsync(); await dispatcher.RunBatchAsync(Noon,default); Assert.Equal(0,sender.Calls); }
    }
    [Fact] public async Task Removed_client_access_prevents_delivery()
    {
        var (db, user, _, sender, dispatcher) = await Setup(); using (db) {
        var alert=Notification.Create(Guid.NewGuid(),user.Id,Guid.NewGuid(),"request.created","Private client update","Details","/client/requests",Noon);
        db.Notifications.Add(alert); await db.SaveChangesAsync();
        foreach(var job in db.NotificationEmailDeliveries.Local) job.NextAttemptAtUtc=Noon.AddMinutes(-1);
        await db.SaveChangesAsync(); await dispatcher.RunBatchAsync(Noon,default);
        Assert.Equal("access_removed",(await db.NotificationEmailDeliveries.SingleAsync(x=>x.NotificationId==alert.Id)).FailureCode); Assert.Equal(1,sender.Calls); }
    }
}
