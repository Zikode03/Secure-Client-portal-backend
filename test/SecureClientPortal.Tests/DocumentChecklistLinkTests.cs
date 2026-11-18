using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Domain.Modules.Documents;
using SecureClientPortal.Backend.Domain.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Infrastructure.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Models;
namespace SecureClientPortal.Backend.Tests;
public sealed class DocumentChecklistLinkTests {
    private static PortalDbContext Db()=>new(new DbContextOptionsBuilder<PortalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    [Fact] public async Task ReceivedStatementLinksToItsOwnMonthWithoutReviewOrApproval() {
        await using var db=Db();var client=Guid.NewGuid();var pack=MonthlyPack.Create(Guid.NewGuid(),client,2026,11);db.MonthlyPacks.Add(pack);
        var slot=DocumentSlot.Create(Guid.NewGuid(),pack.Id,client,"bank_statement","Bank statement",true,null);db.DocumentSlots.Add(slot);
        var doc=Document.CreateUploaded(Guid.NewGuid(),client,pack.Id,"Statement.pdf","bank_statement",null,"application/pdf",100,"private",Guid.NewGuid());db.Documents.Add(doc);
        var reminder=Notification.Create(Guid.NewGuid(),Guid.NewGuid(),client,"document.overdue.reminder","Missing bank statement","Missing",$"/client/packs?slot={slot.Id}");db.Notifications.Add(reminder);await db.SaveChangesAsync();
        await DocumentChecklistLinker.StageAsync(db,doc,slot.Id,null,"system");await db.SaveChangesAsync();
        Assert.Equal(slot.Id,doc.DocumentSlotId);Assert.Equal(doc.Id,slot.CurrentDocumentId);Assert.Equal("draft",slot.Status);Assert.Equal("uploaded",doc.Status);Assert.False(doc.IsFiled);Assert.Empty(db.ReviewDecisions);Assert.True(reminder.IsRead);
        await DocumentChecklistLinker.StageAsync(db,doc,slot.Id,null,"system");await db.SaveChangesAsync();Assert.Single(db.AuditLogs);
    }
    [Fact] public async Task ExplicitLinkClassifiesGeneralInvoiceButNeverCreatesAnApproval() {
        await using var db=Db();var client=Guid.NewGuid();var pack=MonthlyPack.Create(Guid.NewGuid(),client,2026,11);db.MonthlyPacks.Add(pack);
        var slot=DocumentSlot.Create(Guid.NewGuid(),pack.Id,client,"invoices","Invoices",true,null);db.DocumentSlots.Add(slot);
        var doc=Document.CreateUploaded(Guid.NewGuid(),client,pack.Id,"Invoice.pdf","general",null,"application/pdf",100,"private",Guid.NewGuid());db.Documents.Add(doc);await db.SaveChangesAsync();
        await DocumentChecklistLinker.StageAsync(db,doc,slot.Id,Guid.NewGuid(),"accountant");await db.SaveChangesAsync();
        Assert.Equal("invoices",doc.Category);Assert.True(doc.InvoiceReviewRequired);Assert.Equal("uploaded",doc.Status);Assert.Equal("draft",slot.Status);
    }
    [Fact] public async Task LinkingCannotCrossMonthsOrOverwriteReviewedRequirements() {
        await using var db=Db();var client=Guid.NewGuid();var pack=MonthlyPack.Create(Guid.NewGuid(),client,2026,11);var other=MonthlyPack.Create(Guid.NewGuid(),client,2026,10);db.MonthlyPacks.AddRange(pack,other);
        var slot=DocumentSlot.Create(Guid.NewGuid(),pack.Id,client,"invoices","Invoices",true,null);var wrong=DocumentSlot.Create(Guid.NewGuid(),other.Id,client,"invoices","Invoices",true,null);db.DocumentSlots.AddRange(slot,wrong);
        var doc=Document.CreateUploaded(Guid.NewGuid(),client,pack.Id,"Invoice.pdf","invoices",null,"application/pdf",100,"private",Guid.NewGuid());db.Documents.Add(doc);await db.SaveChangesAsync();
        await Assert.ThrowsAsync<AppValidationException>(()=>DocumentChecklistLinker.StageAsync(db,doc,wrong.Id,null,"system"));
        slot.MarkDraft(Guid.NewGuid());slot.Submit(Guid.NewGuid());
        await Assert.ThrowsAsync<AppValidationException>(()=>DocumentChecklistLinker.StageAsync(db,doc,slot.Id,null,"system"));Assert.Null(doc.DocumentSlotId);
    }
    [Fact] public void MultipleFilesKeepRequirementInReviewUntilEveryFileIsAccepted() {
        var client=Guid.NewGuid();var reviewer=Guid.NewGuid();var pack=MonthlyPack.Create(Guid.NewGuid(),client,2026,11);
        var slot=DocumentSlot.Create(Guid.NewGuid(),pack.Id,client,"receipts","Receipts",true,null);
        var first=Document.CreateUploaded(Guid.NewGuid(),client,pack.Id,"First.pdf","receipts",slot.Id,"application/pdf",100,"private-1",reviewer);
        var second=Document.CreateUploaded(Guid.NewGuid(),client,pack.Id,"Second.pdf","receipts",slot.Id,"application/pdf",100,"private-2",reviewer);
        slot.MarkDraft(second.Id);slot.Submit(reviewer);var linked=new[]{first,second};
        DocumentReviewPolicy.ApplyDecision(second,slot,pack,"accepted",reviewer,"accountant",null,null,DateTime.UtcNow,linked);
        Assert.Equal("under_review",slot.Status);Assert.Equal(first.Id,slot.CurrentDocumentId);
        Assert.Throws<DomainRuleException>(()=>pack.CloseIfReady(new[]{slot}));
        DocumentReviewPolicy.ApplyDecision(first,slot,pack,"accepted",reviewer,"accountant",null,null,DateTime.UtcNow,linked);
        Assert.Equal("accepted",slot.Status);pack.CloseIfReady(new[]{slot});Assert.Equal("closed",pack.Status);
    }
    [Fact] public async Task CloseServiceRejectsUnreviewedSiblingEvenIfLegacySlotIsAccepted() {
        await using var db=Db();var client=Guid.NewGuid();var actor=Guid.NewGuid();
        db.Clients.Add(Client.Create(client,"Closing test","Company","Contact","close@example.invalid",ClientStatus.Active));
        var pack=MonthlyPack.Create(Guid.NewGuid(),client,2026,11);db.MonthlyPacks.Add(pack);
        var slot=DocumentSlot.Create(Guid.NewGuid(),pack.Id,client,"receipts","Receipts",true,null);db.DocumentSlots.Add(slot);
        var first=Document.CreateUploaded(Guid.NewGuid(),client,pack.Id,"First.pdf","receipts",slot.Id,"application/pdf",100,"private-1",actor);
        var second=Document.CreateUploaded(Guid.NewGuid(),client,pack.Id,"Second.pdf","receipts",slot.Id,"application/pdf",100,"private-2",actor);second.Accept();db.Documents.AddRange(first,second);
        slot.MarkDraft(second.Id);slot.Submit(actor);slot.Accept(second.Id);await db.SaveChangesAsync();
        var user=new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(new[]{new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier,actor.ToString()),new System.Security.Claims.Claim("role","admin"),new System.Security.Claims.Claim("role_scope","admin"),new System.Security.Claims.Claim("permission","documents.review"),new System.Security.Claims.Claim("permission","access.admin")},"test"));
        var result=await new MonthlyPackService(db).CloseAsync(pack.Id.ToString(),user);
        Assert.True(result.invalid);Assert.Contains("every document",result.error);Assert.NotEqual("closed",pack.Status);
    }

}
