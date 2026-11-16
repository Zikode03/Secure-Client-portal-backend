using System.Data;
using System.Net.Mail;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts.Modules.Migration;
using SecureClientPortal.Backend.Application.Modules.Documents;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Domain.Modules.Documents;
using SecureClientPortal.Backend.Domain.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Models;
namespace SecureClientPortal.Backend.Infrastructure.Modules.Migration;

public sealed class DataMigrationService(PortalDbContext db, IFileStorage storage)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private sealed class State {
        public List<ClientState> Clients {get;set;}=[];
        public List<FileState> Documents {get;set;}=[];
    }
    private sealed class ClientState {
        public Guid Id {get;set;}=Guid.NewGuid(); public int SourceRow {get;set;}
        public MigrationClientInput Values {get;set;}=new("","","","","","","");
        public bool Skip {get;set;} public Guid? ImportedId {get;set;} public string[] Errors {get;set;}=[];
    }
    private sealed class FileState {
        public Guid Id {get;set;} public StoredFile File {get;set;}=null!; public string Hash {get;set;}="";
        public MigrationDocumentInput Values {get;set;}=new(null,null,null,"general","",false,false,false);
        public Guid? ImportedId {get;set;} public string[] Errors {get;set;}=[]; public string[] Warnings {get;set;}=[];
    }
    public async Task<MigrationSummary[]> ListAsync(ClaimsPrincipal actor, CancellationToken ct) {
        DemandAdmin(actor);
        return (await db.Set<DataMigrationBatch>().AsNoTracking().OrderByDescending(b=>b.CreatedAtUtc).Take(100).ToListAsync(ct))
            .Select(b=>Summary(b,Read(b))).ToArray();
    }
    public async Task<MigrationBatchDetail> CreateAsync(CreateMigrationRequest request, ClaimsPrincipal actor, CancellationToken ct) {
        DemandAdmin(actor);
        if(request.RequestId==Guid.Empty || string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length>200)throw new AppValidationException("Provide a batch name up to 200 characters and a request id.");
        var previous=await db.Set<DataMigrationBatch>().FindAsync([request.RequestId],ct);
        if(previous is not null)return Detail(previous,Read(previous));
        var batch=new DataMigrationBatch {Id=request.RequestId,Name=request.Name.Trim(),CreatedByUserId=ActorId(actor),StateJson=JsonSerializer.Serialize(new State(),Json)};
        db.Add(batch); Audit(batch,actor,"migration.created");await db.SaveChangesAsync(ct);return Detail(batch,Read(batch));
    }
    public async Task<MigrationBatchDetail> GetAsync(Guid id, ClaimsPrincipal actor, CancellationToken ct) {
        var b=await Find(id,actor,ct);var s=Read(b);if(b.Status=="draft")await Validate(s,ct);return Detail(b,s);
    }
    public async Task<MigrationBatchDetail> SpreadsheetAsync(Guid id,int version,IFormFile file,ClaimsPrincipal actor,CancellationToken ct) {
        var b=await Find(id,actor,ct);Editable(b,version);var s=Read(b);
        if(s.Clients.Count>0)throw new AppValidationException("This batch already has a client spreadsheet. Correct its rows here, or start a new batch.");
        var rows=await ClientSpreadsheetReader.ReadAsync(file,ct);
        s.Clients=rows.Select(r=>new ClientState {SourceRow=r.Row,Values=r.Values}).ToList();
        return await Save(b,s,actor,"migration.clients_staged",ct);
    }
    public async Task<MigrationBatchDetail> EditClientAsync(Guid id,Guid rowId,EditMigrationClientRequest request,ClaimsPrincipal actor,CancellationToken ct) {
        var b=await Find(id,actor,ct);Editable(b,request.Version);var s=Read(b);
        var row=s.Clients.SingleOrDefault(r=>r.Id==rowId)??throw new AppValidationException("Client row not found.");
        row.Values=Clean(request.Values);row.Skip=request.Skip;return await Save(b,s,actor,"migration.client_corrected",ct);
    }
    public async Task<MigrationBatchDetail> UploadAsync(Guid id,Guid fileId,int version,IFormFile file,ClaimsPrincipal actor,CancellationToken ct) {
        var b=await Find(id,actor,ct);var s=Read(b);
        // Stable ids make a retried upload safe after a lost response.
        if(s.Documents.Any(d=>d.Id==fileId))return Detail(b,s);
        Editable(b,version);
        if(fileId==Guid.Empty || s.Documents.Count>=100 || file.Length is <=0 or >20971520)throw new AppValidationException("Upload at most 100 files per batch, up to 20 MB per file.");
        string hash;
        await using(var stream=file.OpenReadStream()) hash=Convert.ToHexString(await SHA256.HashDataAsync(stream,ct));
        var stored=await storage.SaveAsync(file,b.Id.ToString(),ct);
        try {
            s.Documents.Add(new FileState {Id=fileId,File=stored,Hash=hash,Values=new(null,null,null,"general",stored.OriginalFileName,false,false,false)});
            return await Save(b,s,actor,"migration.document_staged",ct);
        } catch { await storage.DeleteAsync(stored.StorageKey,CancellationToken.None);throw; }
    }
    public async Task<MigrationBatchDetail> EditDocumentAsync(Guid id,Guid fileId,EditMigrationDocumentRequest request,ClaimsPrincipal actor,CancellationToken ct) {
        var b=await Find(id,actor,ct);Editable(b,request.Version);var s=Read(b);
        var row=s.Documents.SingleOrDefault(r=>r.Id==fileId)??throw new AppValidationException("Document row not found.");
        var v=request.Values;
        row.Values=v with {DisplayName=(v.DisplayName??"").Trim(),Category=(v.Category??"").Trim()};
        return await Save(b,s,actor,"migration.document_corrected",ct);
    }
    public async Task<StoredFileContent> DownloadAsync(Guid id,Guid fileId,ClaimsPrincipal actor,CancellationToken ct) {
        var b=await Find(id,actor,ct);var row=Read(b).Documents.SingleOrDefault(r=>r.Id==fileId)??throw new AppValidationException("Document row not found.");
        return await storage.OpenReadAsync(row.File.StorageKey,ct)??throw new AppValidationException("The staged file could not be found. Do not approve this batch.");
    }
    public async Task<MigrationBatchDetail> ApproveAsync(Guid id,ApproveMigrationRequest request,ClaimsPrincipal actor,CancellationToken ct) {
        var b=await Find(id,actor,ct);
        if(b.Status=="approved")return Detail(b,Read(b));
        Editable(b,request.Version);
        if(!request.ConfirmOwnershipAndPeriods)throw new AppValidationException("Confirm ownership and historical periods before approving.");
        // Serialise approval against other imports and ordinary profile/document writes.
        await using var tx=db.Database.IsRelational()?await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct):null;
        var s=Read(b);await Validate(s,ct);
        if(s.Clients.Any(r=>!r.Skip&&r.Errors.Length>0)||s.Documents.Any(r=>!r.Values.Skip&&(r.Errors.Length>0 || r.Warnings.Length>0&&!r.Values.DuplicateAcknowledged)))
            throw new AppValidationException("Correct or skip every invalid row and resolve duplicate warnings before approval.");
        if(!s.Clients.Any(r=>!r.Skip)&&!s.Documents.Any(r=>!r.Values.Skip))throw new AppValidationException("There are no rows to import.");
        foreach(var row in s.Clients.Where(r=>!r.Skip)) {
            var v=Clean(row.Values);var accountant=await Accountant(v.AccountantEmail,ct)??throw new AppValidationException("Accountant is no longer available.");
            var client=Client.Create(Guid.NewGuid(),v.Name,v.EntityType,v.ContactName,v.ContactEmail,ClientStatus.Active);
            client.UpdateBusinessProfile(v.Name,"",v.RegistrationNumber,"","",v.ContactName,v.ContactEmail,"","","","South Africa",v.Industry,"");
            client.AssignAccountant(accountant.Id);db.Clients.Add(client);db.ClientAssignments.Add(ClientAssignment.Create(Guid.NewGuid(),accountant.Id,client.Id));row.ImportedId=client.Id;
        }
        foreach(var row in s.Documents.Where(r=>!r.Values.Skip)) {
            var v=row.Values;var clientId=v.ClientId!.Value;
            var content=await storage.OpenReadAsync(row.File.StorageKey,ct) ?? throw new AppValidationException("A staged file is missing; import was not approved.");
            await content.Stream.DisposeAsync();
            var pack=db.MonthlyPacks.Local.FirstOrDefault(p=>p.ClientId==clientId&&p.Year==v.Year&&p.Month==v.Month)
                ??await db.MonthlyPacks.SingleOrDefaultAsync(p=>p.ClientId==clientId&&p.Year==v.Year&&p.Month==v.Month,ct);
            if(pack is null) {pack=MonthlyPack.Create(Guid.NewGuid(),clientId,v.Year!.Value,v.Month!.Value);db.MonthlyPacks.Add(pack);}
            if(pack.Status=="closed")throw new AppValidationException("A target month was closed. Reopen it through the normal workflow before importing.");
            // No slot, review decision, acceptance, filing, submission or notification is fabricated.
            var doc=Document.CreateUploaded(Guid.NewGuid(),clientId,pack.Id,v.DisplayName,v.Category,null,row.File.ContentType,row.File.SizeBytes,row.File.StorageKey,ActorId(actor));
            doc.SetContentSha256(row.Hash);db.Documents.Add(doc);
            db.DocumentVersions.Add(DocumentVersion.Create(Guid.NewGuid(),doc.Id,1,doc.Name,row.File.OriginalFileName,row.File.StoredFileName,doc.FileType,doc.SizeBytes,doc.StorageKey,true,ActorId(actor)));
            row.ImportedId=doc.Id;
        }
        b.Status="approved";var result=await Save(b,s,actor,"migration.approved",ct,false);
        if(tx is not null)await tx.CommitAsync(ct);return result;
    }
    private async Task Validate(State s,CancellationToken ct) {
        var existing=await db.Clients.AsNoTracking().ToListAsync(ct);
        var accountantRoles=await db.RoleDefinitions.Where(r=>r.IsActive&&r.Scope=="accountant").Select(r=>r.Name).ToListAsync(ct);
        var accountantEmails=(await db.Users.AsNoTracking().Where(u=>accountantRoles.Contains(u.Role)).ToListAsync(ct))
            .Where(u=>UserSecurityProfile.GetStatus(u.SecurityJson)=="active").Select(u=>u.Email).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach(var r in s.Clients) {
            r.Values=Clean(r.Values);var v=r.Values;var errors=new List<string>();
            if(v.Name.Length is <1 or >200)errors.Add("Client name is required (maximum 200 characters).");
            if(v.EntityType.Length is <1 or >100 || v.Industry.Length is <1 or >100)errors.Add("Entity type and industry are required (maximum 100 characters each).");
            if(v.ContactName.Length is <1 or >200)errors.Add("Contact name is required (maximum 200 characters).");
            if(!Email(v.ContactEmail)||v.ContactEmail.Length>320)errors.Add("Enter a valid contact email.");
            if(v.RegistrationNumber.Length>100)errors.Add("Registration number must be at most 100 characters.");
            if(!accountantEmails.Contains(v.AccountantEmail))errors.Add("Accountant email must match an active accountant account.");
            bool Matches(string name,string email,string reg)=>
                v.RegistrationNumber.Length>0&&string.Equals(reg,v.RegistrationNumber,StringComparison.OrdinalIgnoreCase)
                ||string.Equals(name,v.Name,StringComparison.OrdinalIgnoreCase)&&string.Equals(email,v.ContactEmail,StringComparison.OrdinalIgnoreCase);
            if(existing.Any(c=>Matches(c.Name,c.Email,c.RegistrationNumber)))errors.Add("Possible existing client. Skip this row and use the existing profile; existing profiles are never overwritten.");
            if(s.Clients.Any(other=>other.Id!=r.Id&&!other.Skip&&Matches(other.Values.Name,other.Values.ContactEmail,other.Values.RegistrationNumber)))errors.Add("Possible duplicate client in this spreadsheet. Keep one row and skip the other.");
            r.Errors=errors.ToArray();
        }
        foreach(var r in s.Documents) {
            var v=r.Values;var errors=new List<string>();var warnings=new List<string>();
            if(!v.ClientId.HasValue||!existing.Any(c=>c.Id==v.ClientId))errors.Add("Select an existing client. Approve imported clients first in a separate batch.");
            var current=DateTime.UtcNow;
            if(v.Year is null or <1900 || v.Month is null or <1 or >12 || v.Year>current.Year || v.Year==current.Year&&v.Month>current.Month)errors.Add("Choose a valid historical month, no later than this month.");
            if(string.IsNullOrWhiteSpace(v.DisplayName)||v.DisplayName.Length>200||v.DisplayName.IndexOfAny(['\\','/',':','*','?','"','<','>','|'])>=0||v.DisplayName.Any(char.IsControl)||!string.Equals(Path.GetExtension(v.DisplayName),Path.GetExtension(r.File.OriginalFileName),StringComparison.OrdinalIgnoreCase))errors.Add("Use a document name up to 200 characters with the original extension and no path characters.");
            if(!Categories.Contains(v.Category))errors.Add("Choose a supported record type.");
            if(!v.OwnershipConfirmed)errors.Add("Confirm this document belongs to the selected client and month.");
            if(v.ClientId.HasValue) {
                if(await db.MonthlyPacks.AnyAsync(p=>p.ClientId==v.ClientId&&p.Year==v.Year&&p.Month==v.Month&&p.Status=="closed",ct))errors.Add("The target month is closed; reopen it before importing.");
                if(await db.Documents.AnyAsync(d=>d.ClientId==v.ClientId&&(d.ContentSha256==r.Hash || d.Name==v.DisplayName&&d.SizeBytes==r.File.SizeBytes),ct))warnings.Add("Possible duplicate already exists for this client (matching content or name and size).");
                if(s.Documents.Any(o=>o.Id!=r.Id&&!o.Values.Skip&&o.Values.ClientId==v.ClientId&&o.Hash==r.Hash))warnings.Add("Matching file content occurs elsewhere in this batch for this client.");
            }
            r.Errors=errors.ToArray();r.Warnings=warnings.ToArray();
        }
    }
    public static readonly string[] Categories=["general","bank_statement","invoices","receipts","signed_documents","compliance_record","payroll_summary","tax_working_papers","proof_of_payment","credit_notes","debit_notes"];
    private async Task<User?> Accountant(string email,CancellationToken ct) {
        if(!Email(email))return null;
        var roleNames=await db.RoleDefinitions.Where(r=>r.IsActive&&r.Scope=="accountant").Select(r=>r.Name).ToListAsync(ct);
        var normalized=email.Trim().ToLowerInvariant();
        var u=await db.Users.SingleOrDefaultAsync(u=>u.Email.ToLower()==normalized&&roleNames.Contains(u.Role),ct);
        return u is not null&&UserSecurityProfile.GetStatus(u.SecurityJson)=="active"?u:null;
    }
    private static bool Email(string value)=>!string.IsNullOrWhiteSpace(value)&&MailAddress.TryCreate(value,out var parsed)&&parsed.Address==value;
    private static MigrationClientInput Clean(MigrationClientInput v)=>new((v.Name??"").Trim(),(v.EntityType??"").Trim(),(v.Industry??"").Trim(),(v.ContactName??"").Trim(),(v.ContactEmail??"").Trim().ToLowerInvariant(),(v.RegistrationNumber??"").Trim(),(v.AccountantEmail??"").Trim().ToLowerInvariant());
    private static Guid ActorId(ClaimsPrincipal actor) => actor.GetUserId() ?? throw new UnauthorizedAccessException("Authenticated user id is required.");
    private static void DemandAdmin(ClaimsPrincipal actor) {if(!actor.IsAdmin() || actor.GetUserId() is null)throw new UnauthorizedAccessException("Only administrators can manage migration batches.");}
    private async Task<DataMigrationBatch> Find(Guid id,ClaimsPrincipal actor,CancellationToken ct) {
        DemandAdmin(actor);return await db.Set<DataMigrationBatch>().FindAsync([id],ct)??throw new AppValidationException("Migration batch not found.");
    }
    private static void Editable(DataMigrationBatch b,int version) {
        if(b.Status!="draft")throw new AppValidationException("Approved batches cannot be changed. Use the normal document correction workflow.");
        if(b.Version!=version)throw new DbUpdateConcurrencyException("This batch changed. Refresh before editing or approving.");
    }
    private async Task<MigrationBatchDetail> Save(DataMigrationBatch b,State s,ClaimsPrincipal actor,string action,CancellationToken ct,bool validate=true) {
        if(validate)await Validate(s,ct);
        b.StateJson=JsonSerializer.Serialize(s,Json);b.Version++;b.UpdatedAtUtc=DateTime.UtcNow;Audit(b,actor,action);
        await db.SaveChangesAsync(ct);return Detail(b,s);
    }
    private void Audit(DataMigrationBatch b,ClaimsPrincipal actor,string action)=>db.AuditLogs.Add(AuditLog.Create(Guid.NewGuid(),ActorId(actor),"admin",action,"data_migration",b.Id,null,JsonSerializer.Serialize(new {b.Version,b.Status},Json)));
    private static State Read(DataMigrationBatch b)=>JsonSerializer.Deserialize<State>(b.StateJson,Json)??new();
    private static MigrationSummary Summary(DataMigrationBatch b,State s)=>new(b.Id,b.Name,b.Status,b.Version,b.CreatedAtUtc,b.UpdatedAtUtc,s.Clients.Count,s.Documents.Count,s.Clients.Count(r=>r.ImportedId.HasValue),s.Documents.Count(r=>r.ImportedId.HasValue));
    private static MigrationBatchDetail Detail(DataMigrationBatch b,State s)=>new(Summary(b,s),s.Clients.Select(r=>new MigrationClientRow(r.Id,r.SourceRow,r.Values,r.Skip,r.ImportedId,r.Errors)).ToArray(),s.Documents.Select(r=>new MigrationDocumentRow(r.Id,r.File.OriginalFileName,r.File.SizeBytes,r.Values,r.ImportedId,r.Errors,r.Warnings)).ToArray());
}
