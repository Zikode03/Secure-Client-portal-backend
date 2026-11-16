using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application.Contracts.Modules.Migration;
using SecureClientPortal.Backend.Infrastructure.Modules.Migration;
namespace SecureClientPortal.Backend.Api.Modules.Migration;

[ApiController,Route("api/admin/migrations"),Authorize(Policy="AdminOnly")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class DataMigrationController(DataMigrationService service):ControllerBase
{
    [HttpGet] public async Task<ActionResult<MigrationSummary[]>> List(CancellationToken ct)=>Ok(await service.ListAsync(User,ct));
    [HttpPost] public async Task<ActionResult<MigrationBatchDetail>> Create(CreateMigrationRequest request,CancellationToken ct)=>Ok(await service.CreateAsync(request,User,ct));
    [HttpGet("{id:guid}")] public async Task<ActionResult<MigrationBatchDetail>> Get(Guid id,CancellationToken ct)=>Ok(await service.GetAsync(id,User,ct));
    [HttpPost("{id:guid}/clients"),RequestSizeLimit(2200000)]
    public Task<ActionResult<MigrationBatchDetail>> Spreadsheet(Guid id,[FromForm]int version,IFormFile file,CancellationToken ct)=>Change(()=>service.SpreadsheetAsync(id,version,file,User,ct));
    [HttpPut("{id:guid}/clients/{rowId:guid}")]
    public Task<ActionResult<MigrationBatchDetail>> EditClient(Guid id,Guid rowId,EditMigrationClientRequest request,CancellationToken ct)=>Change(()=>service.EditClientAsync(id,rowId,request,User,ct));
    [HttpPost("{id:guid}/documents/{fileId:guid}"),RequestSizeLimit(21100000)]
    public Task<ActionResult<MigrationBatchDetail>> Upload(Guid id,Guid fileId,[FromForm]int version,IFormFile file,CancellationToken ct)=>Change(()=>service.UploadAsync(id,fileId,version,file,User,ct));
    [HttpPut("{id:guid}/documents/{fileId:guid}")]
    public Task<ActionResult<MigrationBatchDetail>> EditDocument(Guid id,Guid fileId,EditMigrationDocumentRequest request,CancellationToken ct)=>Change(()=>service.EditDocumentAsync(id,fileId,request,User,ct));
    [HttpGet("{id:guid}/documents/{fileId:guid}/content")]
    public async Task<IActionResult> Download(Guid id,Guid fileId,CancellationToken ct) {
        var file=await service.DownloadAsync(id,fileId,User,ct);
        Response.Headers.ContentDisposition="attachment; filename=\"migration-document\"";
        Response.Headers.XContentTypeOptions="nosniff";
        return File(file.Stream,file.ContentType);
    }
    [HttpPost("{id:guid}/approve")]
    public Task<ActionResult<MigrationBatchDetail>> Approve(Guid id,ApproveMigrationRequest request,CancellationToken ct)=>Change(()=>service.ApproveAsync(id,request,User,ct));
    private async Task<ActionResult<MigrationBatchDetail>> Change(Func<Task<MigrationBatchDetail>> change) {
        try {return Ok(await change());}
        catch(DbUpdateConcurrencyException) {return Conflict(new {error="This batch changed in another session. Refresh before trying again."});}
    }
}
