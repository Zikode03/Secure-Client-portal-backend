using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Models;
using SecureClientPortal.Backend.Domain.Modules.Documents;
using SecureClientPortal.Backend.Domain.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Infrastructure.Modules.UsersRoles.Application;
namespace SecureClientPortal.Backend.Tests;
public sealed class TeamProgressTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static PortalDbContext Db() => new(new DbContextOptionsBuilder<PortalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    [Fact] public async Task Separates_client_progress_from_individual_decisions_and_backup_coverage()
    {
        await using var db=Db();var owner=Guid.NewGuid();var backup=Guid.NewGuid();var client=Guid.NewGuid();var pack=MonthlyPack.Create(Guid.NewGuid(),client,2026,11);
        db.Users.AddRange(User.CreateInvited(owner,"Owner","owner@example.invalid",UserRole.Accountant,"hash","[]",null),User.CreateInvited(backup,"Backup","backup@example.invalid",UserRole.Accountant,"hash","[]",null));
        var business=Client.Create(client,"Business","company","Contact","contact@example.invalid",ClientStatus.Active);business.AssignAccountant(owner);db.Clients.Add(business);
        db.ClientAssignments.AddRange(ClientAssignment.Create(Guid.NewGuid(),owner,client),ClientAssignment.Create(Guid.NewGuid(),backup,client));db.MonthlyPacks.Add(pack);
        var doc=Guid.NewGuid();var accepted=DocumentSlot.Create(Guid.NewGuid(),pack.Id,client,"invoices","Approved",true,null);accepted.MarkDraft(doc);accepted.Submit(owner);accepted.Accept(doc);
        var inReview=DocumentSlot.Create(Guid.NewGuid(),pack.Id,client,"invoices","In review",true,null);inReview.MarkDraft(Guid.NewGuid());inReview.Submit(owner);inReview.MarkUnderReview();
        var waiting=DocumentSlot.Create(Guid.NewGuid(),pack.Id,client,"invoices","Waiting",true,null);
        var na=DocumentSlot.Create(Guid.NewGuid(),pack.Id,client,"invoices","Not applicable",true,null);na.MarkNotApplicable();
        db.DocumentSlots.AddRange(accepted,inReview,waiting,na);
        db.Documents.Add(Document.CreateUploaded(doc,client,pack.Id,"invoice.pdf","invoices",accepted.Id,"application/pdf",10,null,owner));
        db.ReviewDecisions.Add(ReviewDecision.Create(Guid.NewGuid(),doc,"accepted",backup,"accountant",null,null));
        db.AuditLogs.Add(AuditLog.Create(Guid.NewGuid(),backup,"accountant","documents.reviewed","document",doc,client,null));
        await db.SaveChangesAsync(Ct);
        var response=await new TeamProgressService(db).GetAsync(Ct);var primary=response.Accountants.Single(x=>x.AccountantId==owner);var secondary=response.Accountants.Single(x=>x.AccountantId==backup);
        Assert.Equal(1,primary.PrimaryClients);Assert.Equal(3,primary.RequiredItems);Assert.Equal(1,primary.ApprovedItems);Assert.Equal(1,primary.InReview);Assert.Equal(1,primary.WaitingOnClient);Assert.Equal(0,primary.DecisionsToday);
        Assert.Equal(0,secondary.PrimaryClients);Assert.Equal(1,secondary.BackupClients);Assert.Equal(0,secondary.ApprovedItems);Assert.Equal(1,secondary.DecisionsToday);Assert.Equal("invoice.pdf",secondary.LatestActivity!.DocumentName);Assert.Equal("backup",Assert.Single(secondary.Clients).Assignment);
    }
    [Fact] public async Task Covers_old_open_packs_and_legacy_primary_assignments_without_claiming_presence()
    {
        await using var db=Db();var owner=Guid.NewGuid();var id=Guid.NewGuid();db.Users.Add(User.CreateInvited(owner,"Owner","owner@example.invalid",UserRole.Accountant,"hash","[]",null));var client=Client.Create(id,"Business","company","Contact","c@example.invalid",ClientStatus.Active);client.AssignAccountant(owner);db.Clients.Add(client);
        var old=MonthlyPack.Create(Guid.NewGuid(),id,2026,8);var current=MonthlyPack.Create(Guid.NewGuid(),id,2026,11);db.MonthlyPacks.AddRange(old,current);
        db.DocumentSlots.AddRange(DocumentSlot.Create(Guid.NewGuid(),old.Id,id,"invoices","Older missing",true,null),DocumentSlot.Create(Guid.NewGuid(),current.Id,id,"invoices","Current missing",true,null));await db.SaveChangesAsync(Ct);
        var ownerResult=Assert.Single((await new TeamProgressService(db).GetAsync(Ct)).Accountants);Assert.Equal(1,ownerResult.PrimaryClients);Assert.Equal(2,ownerResult.WaitingOnClient);Assert.Equal(2,Assert.Single(ownerResult.Clients).Packs.Count);Assert.Null(ownerResult.LatestActivity);
    }
    [Fact] public async Task Backup_only_client_is_unassigned_and_empty_requirement_counts_are_not_invented()
    {
        await using var db=Db();var owner=Guid.NewGuid();var id=Guid.NewGuid();db.Users.Add(User.CreateInvited(owner,"Backup","b@example.invalid",UserRole.Accountant,"hash","[]",null));db.Clients.Add(Client.Create(id,"Business","company","Contact","c@example.invalid",ClientStatus.Active));db.ClientAssignments.Add(ClientAssignment.Create(Guid.NewGuid(),owner,id));await db.SaveChangesAsync(Ct);
        var result=await new TeamProgressService(db).GetAsync(Ct);Assert.Equal(1,result.UnassignedClients);var person=Assert.Single(result.Accountants);Assert.Equal(0,person.RequiredItems);Assert.Equal(0,person.PrimaryClients);Assert.Empty(Assert.Single(person.Clients).Packs);
    }
}
