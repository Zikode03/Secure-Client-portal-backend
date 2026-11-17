using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using SecureClientPortal.Backend.Application;
using SecureClientPortal.Backend.Application.Contracts.Modules.Assignments;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Domain.Modules.Assignments;
using SecureClientPortal.Backend.Models;
using SecureClientPortal.Backend.Infrastructure.Modules.Assignments.Application;
namespace SecureClientPortal.Backend.Tests;

public sealed class AssignmentWorkflowRegressionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly CurrentUserContext Admin=new(Guid.NewGuid(),"admin","admin",RolePermissions.ForRole("admin"),null,null);
    private static User Staff(string name,bool active=true)
    {
        var id=Guid.NewGuid();var user=User.CreateInvited(id,name,id+"@example.invalid",UserRole.Accountant,"hash","[]",null);
        if(active)user.CompleteSetup(name,"hash");return user;
    }
    private static PortalDbContext Db(string name,InMemoryDatabaseRoot root,SaveChangesInterceptor? interceptor=null)
    {
        var options=new DbContextOptionsBuilder<PortalDbContext>().UseInMemoryDatabase(name,root);
        if(interceptor is not null)options.AddInterceptors(interceptor);return new(options.Options);
    }
    [Fact] public async Task Primary_change_persists_handover_audit_and_private_accountant_notification_together()
    {
        var name=Guid.NewGuid().ToString();var root=new InMemoryDatabaseRoot();var first=Staff("Previous");var target=Staff("New owner");
        var client=Client.Create(Guid.NewGuid(),"Business","Company","Contact","c@example.invalid",ClientStatus.Active);client.AssignAccountant(first.Id);
        await using(var db=Db(name,root))
        {
            db.Users.AddRange(first,target);db.Clients.Add(client);db.ClientAssignments.Add(ClientAssignment.Create(Guid.NewGuid(),first.Id,client.Id));await db.SaveChangesAsync(Ct);
            await new AssignmentService(db).ReassignAsync(new(client.Id,first.Id,target.Id,true,new(" Portfolio rebalance "," Review the November pack. ")),Admin,Ct);
        }
        await using var saved=Db(name,root);
        Assert.Equal(target.Id,(await saved.Clients.SingleAsync(Ct)).AssignedAccountantId);
        var audit=await saved.AuditLogs.SingleAsync(Ct);using var metadata=JsonDocument.Parse(audit.MetadataJson!);
        Assert.Equal("Portfolio rebalance",metadata.RootElement.GetProperty("reason").GetString());
        Assert.Equal("Review the November pack.",metadata.RootElement.GetProperty("message").GetString());
        Assert.True(metadata.RootElement.GetProperty("effectiveAtUtc").GetDateTime()<=DateTime.UtcNow);
        var notification=await saved.Notifications.SingleAsync(Ct);Assert.Equal(target.Id,notification.UserId);
        Assert.Equal("Review the November pack.",notification.Message);Assert.Equal(client.Id,notification.ClientId);
        Assert.Equal("assignment_handover",notification.Type);
    }
    [Fact] public async Task Backup_replacement_does_not_change_primary_and_rejects_stale_updates()
    {
        await using var db=Db(Guid.NewGuid().ToString(),new());var primary=Staff("Primary");var old=Staff("Old backup");var next=Staff("Next backup");
        var client=Client.Create(Guid.NewGuid(),"Business","Company","Contact","c@example.invalid",ClientStatus.Active);client.AssignAccountant(primary.Id);
        db.Users.AddRange(primary,old,next);db.Clients.Add(client);db.ClientAssignments.AddRange(ClientAssignment.Create(Guid.NewGuid(),primary.Id,client.Id),ClientAssignment.Create(Guid.NewGuid(),old.Id,client.Id));await db.SaveChangesAsync(Ct);
        var service=new AssignmentService(db);await service.ReassignAsync(new(client.Id,old.Id,next.Id,false),Admin,Ct);
        Assert.Equal(primary.Id,client.AssignedAccountantId);Assert.False(await db.ClientAssignments.AnyAsync(x=>x.AccountantUserId==old.Id,Ct));Assert.True(await db.ClientAssignments.AnyAsync(x=>x.AccountantUserId==next.Id,Ct));
        await Assert.ThrowsAsync<ArgumentException>(()=>service.ReassignAsync(new(client.Id,old.Id,primary.Id,false),Admin,Ct));
        Assert.Equal(2,await db.ClientAssignments.CountAsync(Ct));
    }
    [Fact] public async Task Failed_backup_save_keeps_previous_coverage_and_does_not_persist_audit_or_messages()
    {
        var name=Guid.NewGuid().ToString();var root=new InMemoryDatabaseRoot();var primary=Staff("Primary");var old=Staff("Backup");var next=Staff("Next");
        var client=Client.Create(Guid.NewGuid(),"Business","Company","Contact","c@example.invalid",ClientStatus.Active);client.AssignAccountant(primary.Id);
        await using(var seed=Db(name,root)) { seed.Users.AddRange(primary,old,next);seed.Clients.Add(client);seed.ClientAssignments.AddRange(ClientAssignment.Create(Guid.NewGuid(),primary.Id,client.Id),ClientAssignment.Create(Guid.NewGuid(),old.Id,client.Id));await seed.SaveChangesAsync(Ct); }
        await using(var failing=Db(name,root,new FailSave()))
            await Assert.ThrowsAsync<InvalidOperationException>(()=>new AssignmentService(failing).ReassignAsync(new(client.Id,old.Id,next.Id,false),Admin,Ct));
        await using var saved=Db(name,root);Assert.True(await saved.ClientAssignments.AnyAsync(x=>x.AccountantUserId==old.Id,Ct));Assert.False(await saved.ClientAssignments.AnyAsync(x=>x.AccountantUserId==next.Id,Ct));Assert.Equal(primary.Id,(await saved.Clients.SingleAsync(Ct)).AssignedAccountantId);Assert.Empty(await saved.AuditLogs.ToListAsync(Ct));
    }
    [Theory][InlineData("invited")][InlineData("disabled")][InlineData("locked")]
    public async Task Inactive_accounts_cannot_replace_coverage(string state)
    {
        await using var db=Db(Guid.NewGuid().ToString(),new());var current=Staff("Current");var target=Staff("Unavailable",false);
        if(state!="invited")target.SetSecurityStatus(state=="locked"?SecurityStatus.Locked:SecurityStatus.Disabled);
        var client=Client.Create(Guid.NewGuid(),"Business","Company","Contact","c@example.invalid",ClientStatus.Active);client.AssignAccountant(current.Id);
        db.Users.AddRange(current,target);db.Clients.Add(client);db.ClientAssignments.Add(ClientAssignment.Create(Guid.NewGuid(),current.Id,client.Id));await db.SaveChangesAsync(Ct);
        var service=new AssignmentService(db);
        await Assert.ThrowsAsync<ArgumentException>(()=>service.ReassignAsync(new(client.Id,current.Id,target.Id,true),Admin,Ct));
        await Assert.ThrowsAsync<ArgumentException>(()=>service.CreateAsync(new(target.Id,client.Id,false),Admin,Ct));
        Assert.Equal(current.Id,client.AssignedAccountantId);Assert.Single(await db.ClientAssignments.ToListAsync(Ct));Assert.Empty(await db.AuditLogs.ToListAsync(Ct));
    }
    [Fact] public async Task Removing_primary_cannot_promote_an_inactive_backup()
    {
        await using var db=Db(Guid.NewGuid().ToString(),new());var primary=Staff("Primary");var inactive=Staff("Inactive",false);
        var client=Client.Create(Guid.NewGuid(),"Business","Company","Contact","c@example.invalid",ClientStatus.Active);client.AssignAccountant(primary.Id);
        var owner=ClientAssignment.Create(Guid.NewGuid(),primary.Id,client.Id);var backup=ClientAssignment.Create(Guid.NewGuid(),inactive.Id,client.Id);
        db.Users.AddRange(primary,inactive);db.Clients.Add(client);db.ClientAssignments.AddRange(owner,backup);await db.SaveChangesAsync(Ct);
        var service=new AssignmentService(db);
        await Assert.ThrowsAsync<DomainRuleException>(()=>service.DeleteAsync(owner.Id.ToString(),Admin,Ct));
        await Assert.ThrowsAsync<ArgumentException>(()=>service.MakePrimaryAsync(backup.Id.ToString(),Admin,Ct));
        Assert.Equal(primary.Id,client.AssignedAccountantId);Assert.Equal(2,await db.ClientAssignments.CountAsync(Ct));
    }
    private sealed class FailSave:SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,InterceptionResult<int> result,CancellationToken cancellationToken=default)=>throw new InvalidOperationException("Injected storage failure.");
    }
}
