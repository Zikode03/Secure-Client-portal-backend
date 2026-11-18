using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts.Modules.Documents;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Domain.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Infrastructure.Modules.MonthlyPacks;
namespace SecureClientPortal.Backend.Api.Modules.Documents;
[ApiController,Route("api/documents"),Authorize(Policy="AccountantOnly")]
[SecureClientPortal.Backend.Api.Security.RequirePermission("documents.upload")]
public sealed class DocumentChecklistController(PortalDbContext db):ControllerBase
{
    [HttpPost("{id:guid}/checklist-link")]
    public async Task<ActionResult<DocumentSlot>> Link(Guid id,LinkDocumentChecklistRequest request,CancellationToken ct)
    {
        var document=await db.Documents.SingleOrDefaultAsync(x=>x.Id==id,ct);
        if(document is null)return NotFound();
        if(!(await User.GetAccessibleClientIdsAsync(db,ct)).Contains(document.ClientId))return Forbid();
        var slot=await DocumentChecklistLinker.StageAsync(db,document,request.DocumentSlotId,User.GetUserId(),User.IsAdmin()?"admin":"accountant",ct);
        await db.SaveChangesAsync(ct);
        return Ok(slot);
    }
}
