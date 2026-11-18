using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Models;
using SecureClientPortal.Backend.Domain.Modules.Documents;
using SecureClientPortal.Backend.Domain.Modules.MonthlyPacks;
using System.Text.Json;
namespace SecureClientPortal.Backend.Infrastructure.Modules.MonthlyPacks;
public static class DocumentChecklistLinker
{
    // Stage changes in the caller's transaction. Receiving a file never approves it.
    public static async Task<DocumentSlot> StageAsync(PortalDbContext db,Document document,Guid slotId,Guid? actorId,string actorRole,CancellationToken ct=default)
    {
        var slot=await db.DocumentSlots.SingleOrDefaultAsync(x=>x.Id==slotId,ct)??throw new AppValidationException("Checklist item not found.");
        if(slot.ClientId!=document.ClientId||slot.MonthlyPackId!=document.MonthlyPackId)throw new AppValidationException("Choose a checklist item for the same client and month as this document.");
        var pack=await db.MonthlyPacks.SingleAsync(x=>x.Id==slot.MonthlyPackId,ct);
        if(pack.Status is "closed" or "complete")throw new AppValidationException("Reopen this monthly pack before linking documents.");
        if(slot.Status is not ("not_started" or "draft" or "rejected" or "reupload_required"))throw new AppValidationException("This checklist item is already submitted or reviewed. Use the normal correction workflow.");
        if(slot.Category.StartsWith("bank_statement",StringComparison.OrdinalIgnoreCase)&&!document.FileType.Equals("application/pdf",StringComparison.OrdinalIgnoreCase))throw new AppValidationException("A bank statement must be a PDF. Upload the bank-issued PDF for this item.");
        await ClearReceivedRemindersAsync(db,slot,ct);
        if(document.DocumentSlotId==slot.Id&&slot.CurrentDocumentId.HasValue)return slot;
        var previousCategory=document.Category;
        document.LinkToChecklist(slot.Id,slot.Category);
        slot.MarkDraft(document.Id);
        pack.RecalculateStatus(await db.DocumentSlots.Where(x=>x.MonthlyPackId==pack.Id).ToListAsync(ct));
        db.AuditLogs.Add(AuditLog.Create(Guid.NewGuid(),actorId,actorRole,"documents.checklist_linked","document",document.Id,document.ClientId,JsonSerializer.Serialize(new{slotId,monthlyPackId=pack.Id,previousCategory,category=slot.Category})));
        return slot;
    }
    public static async Task ClearReceivedRemindersAsync(PortalDbContext db,DocumentSlot slot,CancellationToken ct=default) {
        var link=$"/client/packs?slot={slot.Id}";
        var reminders=await db.Notifications.Where(x=>x.ClientId==slot.ClientId&&x.LinkUrl==link&&!x.IsRead&&(x.Type=="document.missing.reminder"||x.Type=="document.overdue.reminder")).ToListAsync(ct);
        foreach(var reminder in reminders)reminder.MarkRead();
    }

}
