using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Models;
using SecureClientPortal.Backend.Application.Contracts.Modules.Documents;
using SecureClientPortal.Backend.Domain.Modules.Documents;
namespace SecureClientPortal.Backend.Api.Modules.Documents;
[ApiController,Route("api/documents"),Authorize(Policy="AccountantOnly")]
[SecureClientPortal.Backend.Api.Security.RequirePermission("documents.review")]
public sealed class DocumentArchiveController(PortalDbContext db):ControllerBase {
    [HttpPut("{id:guid}/archive-placement")]
    public async Task<ActionResult<ArchivePlacementResponse>> Place(Guid id,ArchivePlacementRequest request,CancellationToken ct) {
        var document=await db.Documents.SingleOrDefaultAsync(x=>x.Id==id,ct);
        if(document is null)return NotFound();
        if(!(await User.GetAccessibleClientIdsAsync(db,ct)).Contains(document.ClientId))return Forbid();
        if(document.CurrentVersionNumber!=request.ExpectedDocumentVersion)return Conflict(new {error="This document changed. Reload it before organising it."});
        if(document.DocumentSlotId.HasValue)return BadRequest(new {error="This document belongs to a monthly checklist. Correct its checklist association before moving it to another archive section."});
        try{document.RestoreArchivePlacement(request.Scope,request.Year,request.Month);}catch(DomainRuleException ex){return BadRequest(new {error=ex.Message});}
        Guid? packId=null;
        if(request.Scope=="monthly") {
            var pack=await db.MonthlyPacks.SingleOrDefaultAsync(p=>p.ClientId==document.ClientId&&p.Year==request.Year&&p.Month==request.Month,ct);
            if(pack?.Status=="closed"&&!document.IsFiled)return BadRequest(new{error="This month is closed. Reopen it before moving unreviewed records into it."});
            if(pack is null){pack=SecureClientPortal.Backend.Domain.Modules.MonthlyPacks.MonthlyPack.Create(Guid.NewGuid(),document.ClientId,request.Year!.Value,request.Month!.Value);db.MonthlyPacks.Add(pack);}
            packId=pack.Id;
        }
        document.SetArchiveMonthlyPack(packId);
        db.AuditLogs.Add(AuditLog.Create(Guid.NewGuid(),User.GetUserId(),User.IsAdmin()?"admin":"accountant","documents.archive_classified","document",id,document.ClientId,
            JsonSerializer.Serialize(new{scope=document.ArchiveScope,year=document.ArchiveYear,month=document.ArchiveMonth,documentVersion=document.CurrentVersionNumber})));
        await db.SaveChangesAsync(ct);
        return Ok(new ArchivePlacementResponse(document.ArchiveScope,document.ArchiveYear,document.ArchiveMonth));
    }
}
