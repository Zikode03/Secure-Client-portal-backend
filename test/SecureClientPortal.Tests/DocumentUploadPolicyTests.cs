using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts.Modules.Documents;
using SecureClientPortal.Backend.Application.Modules.Documents;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Domain.Modules.Documents;
using SecureClientPortal.Backend.Domain.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Infrastructure.Modules.Documents.Application;
using SecureClientPortal.Backend.Models;

namespace SecureClientPortal.Backend.Tests;
public sealed class DocumentUploadPolicyTests
{
    private static IFormFile File(string name, string type) => new FormFile(new MemoryStream("%PDF-test"u8.ToArray()), 0, 9, "file", name) {Headers = new HeaderDictionary(), ContentType = type};
    [Theory]
    [InlineData("bank_statement", "photo.jpg", "image/jpeg")]
    [InlineData("Bank Statement", "photo.png", "image/png")]
    [InlineData("bank_statement_account_123", "statement.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [InlineData("bank_statement", "renamed.pdf", "image/jpeg")]
    public void BankStatementEvidenceRejectsImagesAndSpreadsheets(string category, string name, string type) {
        var request = new UploadDocumentRequest {ClientId = Guid.NewGuid(), MonthlyPackId = Guid.NewGuid(), DocumentType = category, File = File(name, type)};
        Assert.Throws<AppValidationException>(() => DocumentValidators.ValidateUpload(request));
    }
    [Fact] public void OriginalPdfAndInvoicePhotosKeepTheirOwnRules() {
        DocumentValidators.ValidateUpload(new() {ClientId=Guid.NewGuid(),MonthlyPackId=Guid.NewGuid(),DocumentType="bank_statement",File=File("statement.pdf","application/pdf")});
        DocumentValidators.ValidateUpload(new() {ClientId=Guid.NewGuid(),MonthlyPackId=Guid.NewGuid(),DocumentType="invoices",File=File("invoice.jpg","image/jpeg")});
    }
    [Fact] public async Task ResolvedBankSlotRejectsAnImageDisguisedAsAnInvoiceBeforeStorage() {
        await using var db = new PortalDbContext(new DbContextOptionsBuilder<PortalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var clientId=Guid.NewGuid();
        db.Clients.Add(Client.Create(clientId,"Policy business","Company","Contact","contact@example.invalid",ClientStatus.Active));
        var pack=MonthlyPack.Create(Guid.NewGuid(),clientId,2026,11);db.MonthlyPacks.Add(pack);
        var slot=DocumentSlot.Create(Guid.NewGuid(),pack.Id,clientId,"bank_statement_account_123","Bank Statement",true,null);db.DocumentSlots.Add(slot);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var storage=new Storage(); var service=new DocumentCommandService(db,db,storage);
        var actor=new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.NameIdentifier,Guid.NewGuid().ToString()),new(ClaimTypes.Role,"admin"),new("role_scope","admin")],"test"));
        var request=new UploadDocumentRequest {ClientId=clientId,MonthlyPackId=pack.Id,DocumentSlotId=slot.Id,DocumentType="invoices",File=File("photo.jpg","image/jpeg")};
        await Assert.ThrowsAsync<AppValidationException>(()=>service.UploadAsync(request,actor,TestContext.Current.CancellationToken));
        Assert.Equal(0,storage.Saves);Assert.Empty(db.Documents);
        var accepted=await service.UploadAsync(new UploadDocumentRequest {ClientId=clientId,MonthlyPackId=pack.Id,DocumentSlotId=slot.Id,DocumentType="invoices",File=File("statement.pdf","application/pdf")},actor,TestContext.Current.CancellationToken);
        Assert.Null(accepted.Error);Assert.Equal(1,storage.Saves);Assert.Equal(slot.Category,Assert.Single(db.Documents).Category);
    }
    private sealed class Storage : IFileStorage {
        public int Saves;
        public Task<StoredFile> SaveAsync(IFormFile file,string clientId,CancellationToken ct=default) { Saves++;return Task.FromResult(new StoredFile("private-key",file.FileName,file.FileName,file.ContentType,file.Length)); }
        public Task<StoredFileContent?> OpenReadAsync(string key,CancellationToken ct=default)=>Task.FromResult<StoredFileContent?>(null);
    }
}
