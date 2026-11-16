using System.IO.Compression;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Api.Modules.Migration;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts.Modules.Migration;
using SecureClientPortal.Backend.Application.Modules.Documents;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Domain.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Infrastructure.Modules.Migration;
using SecureClientPortal.Backend.Models;
namespace SecureClientPortal.Backend.Tests;
public sealed class DataMigrationTests
{
    private static CancellationToken Ct=>TestContext.Current.CancellationToken;
    private static ClaimsPrincipal Actor(string role="admin")=>new(new ClaimsIdentity([new(ClaimTypes.NameIdentifier,Guid.NewGuid().ToString()),new(ClaimTypes.Role,role),new("role_scope",role)],"test"));
    private static PortalDbContext Db()=>new(new DbContextOptionsBuilder<PortalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static IFormFile File(string name,string text)=>new FormFile(new MemoryStream(Encoding.UTF8.GetBytes(text)),0,Encoding.UTF8.GetByteCount(text),"file",name){Headers=new HeaderDictionary(),ContentType="application/pdf"};
    private static MigrationClientInput Values(string name="Migration test business")=>new(name,"Company","Services","Contact","contact@example.invalid","REG-123","accountant@example.invalid");
    private static string Csv(MigrationClientInput v)=>ClientSpreadsheetReader.Header+"\n"+string.Join(",",v.Name,v.EntityType,v.Industry,v.ContactName,v.ContactEmail,v.RegistrationNumber,v.AccountantEmail);
    private static void Accountant(PortalDbContext db) {
        db.RoleDefinitions.Add(RoleDefinition.Create("accountant","Accountant","accountant","[]",true));
        var u=User.CreateInvited(Guid.NewGuid(),"Migration test accountant","accountant@example.invalid",UserRole.Accountant,"unused","[]",null);u.CompleteSetup("Migration test accountant","unused");db.Users.Add(u);db.SaveChanges();
    }
    private static Guid Client(PortalDbContext db) {
        var c=Models.Client.Create(Guid.NewGuid(),"Historical test client","Company","Contact","client@example.invalid",ClientStatus.Active);db.Clients.Add(c);db.SaveChanges();return c.Id;
    }
    private static async Task<MigrationBatchDetail> New(DataMigrationService service,ClaimsPrincipal actor)=>await service.CreateAsync(new(Guid.NewGuid(),"Isolated import test"),actor,Ct);
    [Fact] public async Task StagedFilesArePrivate_AndUnknownOwnershipBlocksApproval() {
        await using var db=Db();var storage=new Storage();var service=new DataMigrationService(db,storage);var actor=Actor();var b=await New(service,actor);
        b=await service.UploadAsync(b.Summary.Id,Guid.NewGuid(),b.Summary.Version,File("invoice.pdf","contents"),actor,Ct);
        Assert.Empty(db.Documents);Assert.Empty(db.MonthlyPacks);Assert.Contains(b.Documents[0].Errors,e=>e.Contains("Select an existing client"));
        await Assert.ThrowsAsync<AppValidationException>(()=>service.ApproveAsync(b.Summary.Id,new(b.Summary.Version,true),actor,Ct));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>service.GetAsync(b.Summary.Id,Actor("client"),Ct));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>service.DownloadAsync(b.Summary.Id,b.Documents[0].Id,Actor("accountant"),Ct));
        Assert.DoesNotContain("storageKey",JsonSerializer.Serialize(b),StringComparison.OrdinalIgnoreCase);
    }
    [Fact] public async Task ApprovalCreatesUploadedHistoricalDocument_WithoutReviewOrFiling_AndIsIdempotent() {
        await using var db=Db();var client=Client(db);var service=new DataMigrationService(db,new Storage());var actor=Actor();var b=await New(service,actor);
        b=await service.UploadAsync(b.Summary.Id,Guid.NewGuid(),b.Summary.Version,File("invoice.pdf","contents"),actor,Ct);
        b=await service.EditDocumentAsync(b.Summary.Id,b.Documents[0].Id,new(b.Summary.Version,new(client,2025,1,"invoices","Invoice.pdf",true,false,false)),actor,Ct);
        Assert.Empty(b.Documents[0].Errors);Assert.Empty(db.Documents);
        b=await service.ApproveAsync(b.Summary.Id,new(b.Summary.Version,true),actor,Ct);
        await service.ApproveAsync(b.Summary.Id,new(0,true),actor,Ct);
        var doc=Assert.Single(db.Documents);Assert.Equal("uploaded",doc.Status);Assert.False(doc.IsFiled);Assert.Null(doc.DocumentSlotId);Assert.NotNull(doc.ContentSha256);
        Assert.Single(db.DocumentVersions);Assert.Empty(db.ReviewDecisions);Assert.Empty(db.DocumentSlots);Assert.Empty(db.Notifications);
        Assert.Equal(2025,Assert.Single(db.MonthlyPacks).Year);Assert.Equal("approved",b.Summary.Status);
        await Assert.ThrowsAsync<AppValidationException>(()=>service.EditDocumentAsync(b.Summary.Id,b.Documents[0].Id,new(b.Summary.Version,b.Documents[0].Values),actor,Ct));
    }
    [Fact] public async Task ClientPreviewAndCorrections_ImportProfilesWithoutCreatingAccounts() {
        await using var db=Db();Accountant(db);var service=new DataMigrationService(db,new Storage());var actor=Actor();var b=await New(service,actor);
        b=await service.SpreadsheetAsync(b.Summary.Id,b.Summary.Version,File("clients.csv",Csv(Values() with {ContactEmail="invalid"})),actor,Ct);
        Assert.Empty(db.Clients);Assert.NotEmpty(b.Clients[0].Errors);
        b=await service.EditClientAsync(b.Summary.Id,b.Clients[0].Id,new(b.Summary.Version,Values(),false),actor,Ct);
        Assert.Empty(b.Clients[0].Errors);b=await service.ApproveAsync(b.Summary.Id,new(b.Summary.Version,true),actor,Ct);
        Assert.Single(db.Clients);Assert.Single(db.ClientAssignments);Assert.Single(db.Users);Assert.Empty(db.UserAccessTokens);
        Assert.NotNull(b.Clients[0].ImportedId);
    }
    [Fact] public async Task ExistingClientDuplicateIsBlockedAndCanBeSkipped() {
        await using var db=Db();Accountant(db);var v=Values();db.Clients.Add(Models.Client.Create(Guid.NewGuid(),v.Name,v.EntityType,v.ContactName,v.ContactEmail,ClientStatus.Active));await db.SaveChangesAsync(Ct);
        var service=new DataMigrationService(db,new Storage());var actor=Actor();var b=await New(service,actor);
        b=await service.SpreadsheetAsync(b.Summary.Id,b.Summary.Version,File("clients.csv",Csv(v)),actor,Ct);
        Assert.Contains(b.Clients[0].Errors,e=>e.Contains("existing client"));
        await Assert.ThrowsAsync<AppValidationException>(()=>service.ApproveAsync(b.Summary.Id,new(b.Summary.Version,true),actor,Ct));Assert.Single(db.Clients);
    }
    [Fact] public async Task DuplicateContentWarningsRequireAcknowledgement() {
        await using var db=Db();var client=Client(db);var service=new DataMigrationService(db,new Storage());var actor=Actor();var b=await New(service,actor);
        foreach(var name in new[]{"a.pdf","b.pdf"}) {
            b=await service.UploadAsync(b.Summary.Id,Guid.NewGuid(),b.Summary.Version,File(name,"same"),actor,Ct);
            b=await service.EditDocumentAsync(b.Summary.Id,b.Documents[^1].Id,new(b.Summary.Version,new(client,2025,1,"invoices",name,true,false,false)),actor,Ct);
        }
        Assert.All(b.Documents,d=>Assert.NotEmpty(d.Warnings));
        await Assert.ThrowsAsync<AppValidationException>(()=>service.ApproveAsync(b.Summary.Id,new(b.Summary.Version,true),actor,Ct));
        b=await service.EditDocumentAsync(b.Summary.Id,b.Documents[1].Id,new(b.Summary.Version,b.Documents[1].Values with {Skip=true}),actor,Ct);
        Assert.Empty(b.Documents[0].Warnings);await service.ApproveAsync(b.Summary.Id,new(b.Summary.Version,true),actor,Ct);Assert.Single(db.Documents);
    }
    [Fact] public async Task StaleEditDoesNotOverwriteANewerReview_AndUploadRetryIsSafe() {
        await using var db=Db();var service=new DataMigrationService(db,new Storage());var actor=Actor();var b=await New(service,actor);var fileId=Guid.NewGuid();
        b=await service.UploadAsync(b.Summary.Id,fileId,0,File("a.pdf","x"),actor,Ct);
        b=await service.UploadAsync(b.Summary.Id,fileId,0,File("a.pdf","x"),actor,Ct);Assert.Single(b.Documents);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(()=>service.EditDocumentAsync(b.Summary.Id,fileId,new(0,b.Documents[0].Values),actor,Ct));
    }
    [Fact] public async Task ClosedMonthAndMissingConfirmationCannotBeImported() {
        await using var db=Db();var client=Client(db);var pack=MonthlyPack.Create(Guid.NewGuid(),client,2025,1);pack.Close();db.MonthlyPacks.Add(pack);await db.SaveChangesAsync(Ct);
        var service=new DataMigrationService(db,new Storage());var actor=Actor();var b=await New(service,actor);
        b=await service.UploadAsync(b.Summary.Id,Guid.NewGuid(),0,File("a.pdf","x"),actor,Ct);
        b=await service.EditDocumentAsync(b.Summary.Id,b.Documents[0].Id,new(b.Summary.Version,new(client,2025,1,"invoices","a.pdf",true,false,false)),actor,Ct);
        Assert.Contains(b.Documents[0].Errors,e=>e.Contains("closed"));
        await Assert.ThrowsAsync<AppValidationException>(()=>service.ApproveAsync(b.Summary.Id,new(b.Summary.Version,false),actor,Ct));Assert.Empty(db.Documents);
    }
    [Fact] public async Task CsvSupportsQuotedCommasAndRejectsMalformedQuotes() {
        var rows=ClientSpreadsheetReader.ReadCsv("a,b\r\n\"hello, world\",\"two\"\"quotes\"\r\n");Assert.Equal("hello, world",rows[1][0]);Assert.Equal("two\"quotes",rows[1][1]);
        await Assert.ThrowsAsync<AppValidationException>(()=>ClientSpreadsheetReader.ReadAsync(File("clients.csv",ClientSpreadsheetReader.Header+"\n\"unterminated"),Ct));
    }
    [Fact] public async Task XlsxReadsSharedStringsAndRejectsFormulas() {
        var good=await ClientSpreadsheetReader.ReadAsync(Workbook(false),Ct);Assert.Equal("Workbook client",Assert.Single(good).Values.Name);
        await Assert.ThrowsAsync<AppValidationException>(()=>ClientSpreadsheetReader.ReadAsync(Workbook(true),Ct));
    }
    [Fact] public void EveryMigrationEndpointRequiresAdminPolicy() {
        var policy=Assert.Single(typeof(DataMigrationController).GetCustomAttributes(typeof(AuthorizeAttribute),true).Cast<AuthorizeAttribute>());Assert.Equal("AdminOnly",policy.Policy);
    }
    private static IFormFile Workbook(bool formula) {
        var stream=new MemoryStream();using(var zip=new ZipArchive(stream,ZipArchiveMode.Create,true)) {
            void Add(string name,string xml){using var writer=new StreamWriter(zip.CreateEntry(name).Open());writer.Write(xml);}
            const string ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            Add("xl/workbook.xml",$"<workbook xmlns=\"{ns}\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Clients\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            Add("xl/_rels/workbook.xml.rels","<Relationships><Relationship Id=\"rId1\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
            Add("xl/sharedStrings.xml",$"<sst xmlns=\"{ns}\"><si><t>Workbook client</t></si></sst>");
            string Row(int number,string[] cells)=>$"<row r=\"{number}\">"+string.Concat(cells.Select((v,i)=>$"<c r=\"{(char)('A'+i)}{number}\" t=\"inlineStr\"><is><t>{v}</t></is></c>"))+"</row>";
            var second=Row(2,["Workbook client","Company","Services","Contact","contact@example.invalid","REG1","accountant@example.invalid"]);
            second=second.Replace("<c r=\"A2\" t=\"inlineStr\"><is><t>Workbook client</t></is></c>",formula?"<c r=\"A2\"><f>1+1</f><v>2</v></c>":"<c r=\"A2\" t=\"s\"><v>0</v></c>");
            Add("xl/worksheets/sheet1.xml",$"<worksheet xmlns=\"{ns}\"><sheetData>"+Row(1,ClientSpreadsheetReader.Header.Split(','))+second+"</sheetData></worksheet>");
        }
        stream.Position=0;return new FormFile(stream,0,stream.Length,"file","clients.xlsx");
    }
    private sealed class Storage:IFileStorage {
        private readonly Dictionary<string,byte[]> files=[];
        public async Task<StoredFile> SaveAsync(IFormFile file,string clientId,CancellationToken ct=default) {using var stream=new MemoryStream();await file.CopyToAsync(stream,ct);var key=Guid.NewGuid().ToString();files[key]=stream.ToArray();return new(key,file.FileName,"stored.pdf",file.ContentType,file.Length);}
        public Task<StoredFileContent?> OpenReadAsync(string key,CancellationToken ct=default)=>Task.FromResult(files.TryGetValue(key,out var bytes)?new StoredFileContent(new MemoryStream(bytes),"application/pdf"):null);
        public Task DeleteAsync(string key,CancellationToken ct=default){files.Remove(key);return Task.CompletedTask;}
    }
}
