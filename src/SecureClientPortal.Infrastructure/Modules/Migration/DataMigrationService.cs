using System.Data;
using System.IO.Compression;
using System.Text.RegularExpressions;
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
        public List<string> ArchiveWarnings {get;set;}=[];
        public HashSet<Guid> Archives {get;set;}=[];
        public List<ClientState> Clients {get;set;}=[];
        public List<FileState> Documents {get;set;}=[];
    }
    private sealed class ClientState {
        public Guid Id {get;set;}=Guid.NewGuid(); public int SourceRow {get;set;}
        public MigrationClientInput Values {get;set;}=new("","","","","","","");
        public Guid? ExistingClientId {get;set;} public bool UseExistingClient {get;set;}
        public bool Skip {get;set;} public Guid? ImportedId {get;set;} public string[] Errors {get;set;}=[];
    }
    private sealed class FileState {
        public Guid Id {get;set;} public StoredFile File {get;set;}=null!; public string Hash {get;set;}="";
        public MigrationDocumentInput Values {get;set;}=new(null,null,null,"general","",false,false,false);
        public Guid? ImportedId {get;set;} public string[] Errors {get;set;}=[]; public string[] Warnings {get;set;}=[];
    }
    public async Task<MigrationSummary[]> ListAsync(ClaimsPrincipal actor, CancellationToken ct) {
        DemandImporter(actor);
        return (await db.Set<DataMigrationBatch>().AsNoTracking().Where(b=>actor.IsAdmin()||b.CreatedByUserId==ActorId(actor)).OrderByDescending(b=>b.CreatedAtUtc).Take(100).ToListAsync(ct))
            .Select(b=>Summary(b,Read(b))).ToArray();
    }
    public async Task<MigrationBatchDetail> CreateAsync(CreateMigrationRequest request, ClaimsPrincipal actor, CancellationToken ct) {
        DemandImporter(actor);
        if(request.RequestId==Guid.Empty || string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length>200)throw new AppValidationException("Provide a batch name up to 200 characters and a request id.");
        var previous=await db.Set<DataMigrationBatch>().FindAsync([request.RequestId],ct);
        if(previous is not null){await Find(previous.Id,actor,ct);return Detail(previous,Read(previous));}
        var batch=new DataMigrationBatch {Id=request.RequestId,Name=request.Name.Trim(),CreatedByUserId=ActorId(actor),StateJson=JsonSerializer.Serialize(new State(),Json)};
        db.Add(batch); Audit(batch,actor,"migration.created");await db.SaveChangesAsync(ct);return Detail(batch,Read(batch));
    }
    public async Task<MigrationBatchDetail> GetAsync(Guid id, ClaimsPrincipal actor, CancellationToken ct) {
        var b=await Find(id,actor,ct);var s=Read(b);if(b.Status=="draft")await Validate(s,ct);return Detail(b,s);
    }
    public async Task<MigrationBatchDetail> SpreadsheetAsync(Guid id,int version,IFormFile file,ClaimsPrincipal actor,CancellationToken ct) {
        DemandAdmin(actor);
        var b=await Find(id,actor,ct);Editable(b,version);var s=Read(b);
        if(s.Clients.Count>0)throw new AppValidationException("This batch already has a client spreadsheet. Correct its rows here, or start a new batch.");
        var rows=await ClientSpreadsheetReader.ReadAsync(file,ct);
        s.Clients=rows.Select(r=>new ClientState {SourceRow=r.Row,Values=r.Values}).ToList();
        return await Save(b,s,actor,"migration.clients_staged",ct);
    }
    public async Task<MigrationBatchDetail> EditClientAsync(Guid id,Guid rowId,EditMigrationClientRequest request,ClaimsPrincipal actor,CancellationToken ct) {
        DemandAdmin(actor);
        var b=await Find(id,actor,ct);Editable(b,request.Version);var s=Read(b);
        var row=s.Clients.SingleOrDefault(r=>r.Id==rowId)??throw new AppValidationException("Client row not found.");
        row.Values=Clean(request.Values);row.Skip=request.Skip;row.UseExistingClient=request.UseExistingClient;return await Save(b,s,actor,"migration.client_corrected",ct);
    }
    public async Task<MigrationBatchDetail> UploadAsync(Guid id,Guid fileId,int version,IFormFile file,ClaimsPrincipal actor,CancellationToken ct) {
        var b=await Find(id,actor,ct);var s=Read(b);
        // Stable ids make a retried upload safe after a lost response.
        if(s.Archives.Contains(fileId)||s.Documents.Any(d=>d.Id==fileId))return Detail(b,s);
        Editable(b,version);
        if(Path.GetExtension(file.FileName).Equals(".zip",StringComparison.OrdinalIgnoreCase))return await UploadArchive(b,s,fileId,file,actor,ct);
        if(fileId==Guid.Empty || s.Documents.Count>=2000 || file.Length is <=0 or >20971520)throw new AppValidationException("Upload at most 2,000 files per batch, up to 20 MB per file.");
        string hash;
        await using(var stream=file.OpenReadStream()) hash=Convert.ToHexString(await SHA256.HashDataAsync(stream,ct));
        var stored=await storage.SaveAsync(file,b.Id.ToString(),ct);
        try {
            s.Documents.Add(new FileState {Id=fileId,File=stored,Hash=hash,Values=new(null,null,null,"general",stored.OriginalFileName,false,false,false)});
            return await Save(b,s,actor,"migration.document_staged",ct);
        } catch { await storage.DeleteAsync(stored.StorageKey,CancellationToken.None);throw; }
    }
    private async Task<MigrationBatchDetail> UploadArchive(DataMigrationBatch b,State s,Guid archiveId,IFormFile file,ClaimsPrincipal actor,CancellationToken ct) {
        if(archiveId==Guid.Empty||file.Length is <=0 or >262144000)throw new AppValidationException("Choose a ZIP archive up to 250 MB.");
        var storedFiles=new List<StoredFile>();
        try {
            await using var input=file.OpenReadStream();
            using var archive=new ZipArchive(input,ZipArchiveMode.Read);
            var entries=archive.Entries.Where(e=>!string.IsNullOrEmpty(e.Name)&&!e.FullName.StartsWith("__MACOSX/")).ToArray();
            if(entries.Length==0||s.Documents.Count+entries.Length>2000)throw new AppValidationException("An import supports up to 2,000 documents. Split larger archives by year.");
            long total=0;var paths=new HashSet<string>(StringComparer.Ordinal);
            foreach(var entry in entries) {
                ct.ThrowIfCancellationRequested();
                var path=entry.FullName.Replace('\\','/');
                if(!paths.Add(path))throw new AppValidationException("The archive contains repeated folder paths. Give each file a unique path before importing.");
                if(path.Length>1024||path.StartsWith('/')||path.Split('/').Any(x=>x=="..")||path.Contains(':'))throw new AppValidationException("The archive contains an unsafe folder path.");
                if(entry.Length is <=0 or >20971520||(total+=entry.Length)>524288000)throw new AppValidationException("Each archive document must be up to 20 MB, with at most 500 MB extracted in total.");
                var extension=Path.GetExtension(entry.Name).ToLowerInvariant();
                var mime=extension switch {".pdf"=>"application/pdf",".png"=>"image/png",".jpg" or ".jpeg"=>"image/jpeg",".csv"=>"text/csv",".txt"=>"text/plain",".xlsx"=>"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",".docx"=>"application/vnd.openxmlformats-officedocument.wordprocessingml.document",_=>null};
                if(extension==".json"||entry.Name==".DS_Store") {s.ArchiveWarnings.Add($"Not staged: {path} (archive metadata, not a supported accounting document).");continue;}
                if(mime is null)throw new AppValidationException($"Unsupported archive file: {path}. Remove it or import supported PDF, image, spreadsheet or document files.");
                var key=SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(archiveId+":"+path));
                var rowId=new Guid(key.AsSpan(0,16));
                if(s.Documents.Any(d=>d.Id==rowId))continue;
                await using var contents=entry.Open();using var buffer=new MemoryStream();var chunk=new byte[81920];int read;while((read=await contents.ReadAsync(chunk,ct))>0){if(buffer.Length+read>entry.Length||buffer.Length+read>20971520)throw new AppValidationException("An archive document exceeds its declared size.");await buffer.WriteAsync(chunk.AsMemory(0,read),ct);}
                if(buffer.Length!=entry.Length)throw new AppValidationException("An archive document could not be read completely.");
                var hash=Convert.ToHexString(SHA256.HashData(buffer.ToArray()));buffer.Position=0;
                var upload=new FormFile(buffer,0,buffer.Length,"file",entry.Name){Headers=new HeaderDictionary(),ContentType=mime};
                var stored=await storage.SaveAsync(upload,b.Id.ToString(),ct);storedFiles.Add(stored);
                var suggestion=SuggestArchivePath(path);
                s.Documents.Add(new FileState{Id=rowId,File=stored,Hash=hash,Values=new(null,suggestion.Year,suggestion.Month,suggestion.Category,entry.Name,false,false,false,SourcePath:path)});
            }
            s.Archives.Add(archiveId);
            return await Save(b,s,actor,"migration.archive_staged",ct);
        } catch(InvalidDataException) {foreach(var stored in storedFiles)await storage.DeleteAsync(stored.StorageKey,CancellationToken.None);throw new AppValidationException("This ZIP archive could not be read. Choose a complete, unencrypted ZIP file.");}
        catch {foreach(var stored in storedFiles)await storage.DeleteAsync(stored.StorageKey,CancellationToken.None);throw;}
    }
    public static (int? Year,int? Month,string Category) SuggestArchivePath(string path) {
        var period=Regex.Match(path,@"(?<!\d)((?:19|20)\d{2})[/_ .-]+(0?[1-9]|1[0-2])(?!\d)");
        var normalized=path.ToLowerInvariant();
        var category=normalized.Contains("bank")?"bank_statement":normalized.Contains("receipt")?"receipts":normalized.Contains("invoice")?"invoices":(normalized.Contains("payroll")||normalized.Contains("payslip"))?"payroll_summary":normalized.Contains("tax")?"tax_working_papers":"general";
        return(period.Success?int.Parse(period.Groups[1].Value):null,period.Success?int.Parse(period.Groups[2].Value):null,category);
    }
    public async Task<MigrationBatchDetail> EditDocumentAsync(Guid id,Guid fileId,EditMigrationDocumentRequest request,ClaimsPrincipal actor,CancellationToken ct) {
        var b=await Find(id,actor,ct);Editable(b,request.Version);var s=Read(b);
        var row=s.Documents.SingleOrDefault(r=>r.Id==fileId)??throw new AppValidationException("Document row not found.");
        var v=request.Values;
        if(v.ClientId.HasValue)await DemandClientAccess(v.ClientId.Value,actor,ct);
        if(v.PreviouslyFiled&&!actor.HasPermission("documents.review"))throw new AppForbiddenException("Review permission is required to confirm historical filing.");
        row.Values=v with {DisplayName=(v.DisplayName??"").Trim(),Category=(v.Category??"").Trim()};
        return await Save(b,s,actor,"migration.document_corrected",ct);
    }
    public async Task<MigrationBatchDetail> BulkEditDocumentsAsync(Guid id,BulkMigrationDocumentRequest request,ClaimsPrincipal actor,CancellationToken ct) {
        var b=await Find(id,actor,ct);Editable(b,request.Version);await DemandClientAccess(request.ClientId,actor,ct);
        if(request.PreviouslyFiled&&!actor.HasPermission("documents.review"))throw new AppForbiddenException("Review permission is required to confirm historical filing.");
        var s=Read(b);
        foreach(var row in s.Documents.Where(d=>!d.Values.Skip)) {
            var v=row.Values;
            row.Values=v with {ClientId=request.ClientId,Year=request.Year??v.Year,Month=request.Month??v.Month,
                Category=string.IsNullOrWhiteSpace(request.Category)?v.Category:request.Category,
                OwnershipConfirmed=request.ConfirmOwnershipAndPeriods,PreviouslyFiled=request.PreviouslyFiled,
                HistoricalStatusConfirmed=request.HistoricalStatusConfirmed,PreviousReviewer=request.PreviousReviewer,
                PreviousReviewDateUtc=request.PreviousReviewDateUtc,HistoricalNotes=request.HistoricalNotes,RecordScope=request.RecordScope};
        }
        return await Save(b,s,actor,"migration.documents_bulk_corrected",ct);
    }
    public async Task<StoredFileContent> DownloadAsync(Guid id,Guid fileId,ClaimsPrincipal actor,CancellationToken ct) {
        var b=await Find(id,actor,ct);var row=Read(b).Documents.SingleOrDefault(r=>r.Id==fileId)??throw new AppValidationException("Document row not found.");
        return await storage.OpenReadAsync(row.File.StorageKey,ct)??throw new AppValidationException("The staged file could not be found. Do not approve this batch.");
    }
    public async Task<MigrationBatchDetail> ApproveAsync(Guid id,ApproveMigrationRequest request,ClaimsPrincipal actor,CancellationToken ct) {
        var b=await Find(id,actor,ct);
        if(b.Status=="approved")return Detail(b,Read(b));
        if(!actor.IsAdmin()&&Read(b).Clients.Count>0)throw new AppForbiddenException("Only administrators can import client profiles or change assignments.");
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
            if(row.UseExistingClient) {
                var existingClient=await db.Clients.SingleAsync(c=>c.Id==row.ExistingClientId,ct);
                await new SecureClientPortal.Backend.Infrastructure.Modules.Assignments.Application.AssignmentService(db).CreateAsync(
                    new(accountant.Id,existingClient.Id,true),
                    new SecureClientPortal.Backend.Application.CurrentUserContext(ActorId(actor),"admin","admin",Array.Empty<string>(),null,"Client import: use existing profile"),ct);
                row.ImportedId=existingClient.Id;
                continue;
            }
            var client=Client.Create(Guid.NewGuid(),v.Name,v.EntityType,v.ContactName,v.ContactEmail,ClientStatus.Active);
            client.UpdateBusinessProfile(v.Name,"",v.RegistrationNumber,"","",v.ContactName,v.ContactEmail,"","","","South Africa",v.Industry,"");
            client.AssignAccountant(accountant.Id);db.Clients.Add(client);db.ClientAssignments.Add(ClientAssignment.Create(Guid.NewGuid(),accountant.Id,client.Id));row.ImportedId=client.Id;
        }
        foreach(var row in s.Documents.Where(r=>!r.Values.Skip)) {
            if(row.Values.PreviouslyFiled&&!actor.HasPermission("documents.review"))throw new AppForbiddenException("Review permission is required to confirm historical filing.");
            await DemandClientAccess(row.Values.ClientId!.Value,actor,ct);
            var v=row.Values;var clientId=v.ClientId!.Value;
            var content=await storage.OpenReadAsync(row.File.StorageKey,ct) ?? throw new AppValidationException("A staged file is missing; import was not approved.");
            await content.Stream.DisposeAsync();
            MonthlyPack? pack=null;
            if(v.RecordScope=="monthly") {
                pack=db.MonthlyPacks.Local.FirstOrDefault(p=>p.ClientId==clientId&&p.Year==v.Year&&p.Month==v.Month)
                    ??await db.MonthlyPacks.SingleOrDefaultAsync(p=>p.ClientId==clientId&&p.Year==v.Year&&p.Month==v.Month,ct);
                if(pack is null){pack=MonthlyPack.Create(Guid.NewGuid(),clientId,v.Year!.Value,v.Month!.Value);db.MonthlyPacks.Add(pack);}
                if(pack.Status=="closed"&&!v.PreviouslyFiled)throw new AppValidationException("A target month was closed. Reopen it through the normal workflow before importing.");
            }
            // No slot, review decision, acceptance, filing, submission or notification is fabricated.
            var doc=Document.CreateUploaded(Guid.NewGuid(),clientId,pack?.Id,v.DisplayName,v.Category,null,row.File.ContentType,row.File.SizeBytes,row.File.StorageKey,ActorId(actor));
            doc.SetContentSha256(row.Hash);db.Documents.Add(doc);
            if(v.PreviouslyFiled) {
                doc.ConfirmHistoricalFiling(ActorId(actor));
                db.AuditLogs.Add(AuditLog.Create(Guid.NewGuid(),ActorId(actor),actor.IsAdmin()?"admin":"accountant",
                    "migration.historical_filing_confirmed","document",doc.Id,clientId,
                    JsonSerializer.Serialize(new {migrationId=b.Id,sourcePath=v.SourcePath,previousReviewer=v.PreviousReviewer,
                        previousReviewDateUtc=v.PreviousReviewDateUtc,notes=v.HistoricalNotes,
                        provenance="Historically filed — confirmed during import",confirmedAtUtc=DateTime.UtcNow},Json)));
            }
            if(v.RecordScope!="monthly") {
                doc.RestoreArchivePlacement(v.RecordScope,v.Year,v.Month);
                db.AuditLogs.Add(AuditLog.Create(Guid.NewGuid(),ActorId(actor),actor.IsAdmin()?"admin":"accountant","documents.archive_classified","document",doc.Id,clientId,
                    JsonSerializer.Serialize(new{scope=doc.ArchiveScope,year=doc.ArchiveYear,month=doc.ArchiveMonth,documentVersion=doc.CurrentVersionNumber})));
            }
            var matchingSlots=pack is null?new List<DocumentSlot>():await db.DocumentSlots.Where(x=>x.ClientId==clientId&&x.MonthlyPackId==pack.Id&&x.Category==v.Category&&(x.Status=="not_started"||x.Status=="draft")).Take(2).ToListAsync(ct);
            if(!v.PreviouslyFiled&&string.IsNullOrEmpty(v.SourcePath)&&matchingSlots.Count==1)await SecureClientPortal.Backend.Infrastructure.Modules.MonthlyPacks.DocumentChecklistLinker.StageAsync(db,doc,matchingSlots[0].Id,ActorId(actor),actor.IsAdmin()?"admin":"accountant",ct);
            db.DocumentVersions.Add(DocumentVersion.Create(Guid.NewGuid(),doc.Id,1,doc.Name,row.File.OriginalFileName,row.File.StoredFileName,doc.FileType,doc.SizeBytes,doc.StorageKey,true,ActorId(actor)));
            if(!v.PreviouslyFiled&&(!string.IsNullOrEmpty(v.SourcePath)||v.RecordScope!="monthly")) {
                doc.MarkUnderReview();
                db.AuditLogs.Add(AuditLog.Create(Guid.NewGuid(),ActorId(actor),actor.IsAdmin()?"admin":"accountant","migration.document_sent_for_review","document",doc.Id,clientId,JsonSerializer.Serialize(new{migrationId=b.Id,sourcePath=v.SourcePath},Json)));
            }
            row.ImportedId=doc.Id;
        }
        b.Status="approved";var result=await Save(b,s,actor,"migration.approved",ct,false);
        foreach(var group in s.Documents.Where(r=>r.ImportedId.HasValue&&!r.Values.Skip).GroupBy(r=>r.Values.ClientId!.Value)) {
            var recipients=await db.ResolveNotificationRecipientsAsync(group.Key,"client",ct);
            await db.AddNotificationsAsync(actor,recipients,group.Key,"documents.imported","Documents added to your account",
                $"{group.Count()} historical {(group.Count()==1?"document has":"documents have")} been added to Documents. Check their recorded review or historical filing status in Documents.","/client/documents",new{migrationId=b.Id,count=group.Count()},ct);
        }
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
            var matches=existing.Where(c=>Matches(c.Name,c.Email,c.RegistrationNumber)).ToList();
            r.ExistingClientId=matches.Count==1?matches[0].Id:null;
            if(matches.Count>1)errors.Add("More than one existing client matches this row. Leave it out and check the client profiles before assigning an accountant.");
            else if(matches.Count==1&&!r.UseExistingClient)errors.Add("Possible existing client. Choose Use existing client to assign its accountant without creating another profile, or leave this row out.");
            if(r.UseExistingClient) {
                if(!r.ExistingClientId.HasValue)errors.Add("The existing client could not be uniquely identified. Check the registration number or leave this row out.");
                else {
                    var selected=matches[0];
                    var target=await Accountant(v.AccountantEmail,ct);
                    if(selected.Status!="active")errors.Add("This client is inactive. Reactivate the existing profile before assigning an accountant.");
                    if(target is not null&&selected.AssignedAccountantId!=Guid.Empty&&selected.AssignedAccountantId!=target.Id)
                        errors.Add("This client already has a different primary accountant. Change its assignment on the Assignments page before continuing.");
                }
            }
            if(s.Clients.Any(other=>other.Id!=r.Id&&!other.Skip&&Matches(other.Values.Name,other.Values.ContactEmail,other.Values.RegistrationNumber)))errors.Add("Possible duplicate client in this spreadsheet. Keep one row and skip the other.");
            r.Errors=errors.ToArray();
        }
        foreach(var r in s.Documents) {
            var v=r.Values;var errors=new List<string>();var warnings=new List<string>();
            if(!v.ClientId.HasValue||!existing.Any(c=>c.Id==v.ClientId))errors.Add("Select an existing client. Approve imported clients first in a separate batch.");
            var current=DateTime.UtcNow;
            if(v.RecordScope is not ("monthly" or "annual" or "permanent" or "uncertain"))errors.Add("Choose monthly, annual, permanent or period needs checking.");
            if(v.RecordScope=="monthly"&&(v.Year is null or <1900 || v.Month is null or <1 or >12 || v.Year>current.Year || v.Year==current.Year&&v.Month>current.Month))errors.Add("Choose a valid historical month, no later than this month.");
            if(v.RecordScope=="annual"&&(v.Year is null or <1900 || v.Year>current.Year+1))errors.Add("Choose the financial year ending in a valid year.");
            if(string.IsNullOrWhiteSpace(v.DisplayName)||v.DisplayName.Length>200||v.DisplayName.IndexOfAny(['\\','/',':','*','?','"','<','>','|'])>=0||v.DisplayName.Any(char.IsControl)||!string.Equals(Path.GetExtension(v.DisplayName),Path.GetExtension(r.File.OriginalFileName),StringComparison.OrdinalIgnoreCase))errors.Add("Use a document name up to 200 characters with the original extension and no path characters.");
            if(!Categories.Contains(v.Category))errors.Add("Choose a supported record type.");
            if(v.PreviouslyFiled) {
                if(!v.HistoricalStatusConfirmed)errors.Add("Confirm that these records were reviewed and filed before import.");
                if(string.IsNullOrWhiteSpace(v.PreviousReviewer)&&string.IsNullOrWhiteSpace(v.HistoricalNotes))errors.Add("Name the previous reviewer, or explain why historical review details are unavailable.");
                if(v.PreviousReviewDateUtc>DateTime.UtcNow)errors.Add("The historical review date cannot be in the future.");
                if((v.PreviousReviewer?.Length??0)>200||(v.HistoricalNotes?.Length??0)>2000)errors.Add("Keep reviewer details within 200 characters and notes within 2,000 characters.");
            }
            if(!v.OwnershipConfirmed)errors.Add("Confirm this document belongs to the selected client and month.");
            if(v.ClientId.HasValue) {
                if(v.RecordScope=="monthly"&&!v.PreviouslyFiled&&await db.MonthlyPacks.AnyAsync(p=>p.ClientId==v.ClientId&&p.Year==v.Year&&p.Month==v.Month&&p.Status=="closed",ct))errors.Add("The target month is closed; reopen it before importing.");
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
        DemandImporter(actor);
        var batch=await db.Set<DataMigrationBatch>().FindAsync([id],ct)??throw new AppValidationException("Migration batch not found.");
        if(!actor.IsAdmin()) {
            if(batch.CreatedByUserId!=ActorId(actor))throw new AppForbiddenException("You can only open your own document imports.");
            var state=Read(batch);
            if(state.Clients.Count>0)throw new AppForbiddenException("Only administrators can open client-list imports.");
            foreach(var clientId in state.Documents.Where(r=>r.Values.ClientId.HasValue).Select(r=>r.Values.ClientId!.Value).Distinct())
                await DemandClientAccess(clientId,actor,ct);
        }
        return batch;
    }
    private static void DemandImporter(ClaimsPrincipal actor) {
        if((!actor.IsAdmin()&&!actor.IsAccountant())||actor.GetUserId() is null)throw new AppForbiddenException("Only administrators and accountants can import documents.");
    }
    private async Task DemandClientAccess(Guid clientId,ClaimsPrincipal actor,CancellationToken ct) {
        if(!(await actor.GetAccessibleClientIdsAsync(db,ct)).Contains(clientId))throw new AppForbiddenException("You can only import documents for your assigned clients.");
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
    private void Audit(DataMigrationBatch b,ClaimsPrincipal actor,string action)=>db.AuditLogs.Add(AuditLog.Create(Guid.NewGuid(),ActorId(actor),actor.IsAdmin()?"admin":"accountant",action,"data_migration",b.Id,null,JsonSerializer.Serialize(new {b.Version,b.Status},Json)));
    private static State Read(DataMigrationBatch b)=>JsonSerializer.Deserialize<State>(b.StateJson,Json)??new();
    private static MigrationSummary Summary(DataMigrationBatch b,State s)=>new(b.Id,b.Name,b.Status,b.Version,b.CreatedAtUtc,b.UpdatedAtUtc,s.Clients.Count,s.Documents.Count,s.Clients.Count(r=>r.ImportedId.HasValue&&!r.UseExistingClient),s.Documents.Count(r=>r.ImportedId.HasValue),s.Clients.Count(r=>r.ImportedId.HasValue&&r.UseExistingClient));
    private static MigrationBatchDetail Detail(DataMigrationBatch b,State s)=>new(Summary(b,s),s.Clients.Select(r=>new MigrationClientRow(r.Id,r.SourceRow,r.Values,r.Skip,r.ImportedId,r.Errors,r.ExistingClientId,r.UseExistingClient)).ToArray(),s.Documents.Select(r=>new MigrationDocumentRow(r.Id,r.File.OriginalFileName,r.File.SizeBytes,r.Values,r.ImportedId,r.Errors,r.Warnings)).ToArray(),s.ArchiveWarnings.ToArray());
}
