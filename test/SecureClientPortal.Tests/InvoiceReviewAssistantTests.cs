using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts.Modules.Documents;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Domain.Modules.Documents;
using SecureClientPortal.Backend.Domain.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Infrastructure.Modules.Documents.Application;
using SecureClientPortal.Backend.Models;
namespace SecureClientPortal.Backend.Tests;

public sealed class InvoiceReviewAssistantTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly Guid ClientId = Guid.NewGuid();
    private static ClaimsPrincipal Actor(string role = "admin", Guid? clientId = null) => new(new ClaimsIdentity([
        new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()), new(ClaimTypes.Role, role), new("role_scope", role),
        new("client_ids", JsonSerializer.Serialize(new[] { clientId ?? ClientId }))], "test"));
    private static PortalDbContext Db() => new(new DbContextOptionsBuilder<PortalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static InvoiceReviewValues Values => new("Supplier", "INV-001", "2025-01-15", "ZAR", "1150.00", "", "1000.00", "150.00", "0", true);
    private static Document Seed(PortalDbContext db, string category = "invoices")
    {
        db.Clients.Add(Client.Create(ClientId, "Review test business", "Company", "Contact", "contact@example.invalid", ClientStatus.Active));
        var pack = MonthlyPack.Create(Guid.NewGuid(), ClientId, 2025, 1);
        db.MonthlyPacks.Add(pack);
        var document = Document.CreateUploaded(Guid.NewGuid(), ClientId, pack.Id, "invoice.jpg", category, null, "image/jpeg", 20, "private-key", Guid.NewGuid());
        db.Documents.Add(document);
        db.DocumentVersions.Add(DocumentVersion.Create(Guid.NewGuid(), document.Id, 1, document.Name, document.Name, "stored", "image/jpeg", 20, "private-key", true, Guid.NewGuid()));
        db.SaveChanges();
        return document;
    }
    private static SaveInvoiceReviewRequest Request(int version = 1, int revision = 0, InvoiceReviewValues? values = null, bool confirmed = true, string? note = null) =>
        new(Guid.NewGuid(), version, revision, values ?? Values, [], confirmed, note, "manual");

    [Fact] public void CorrectFiguresPass_AndMismatchRemainsAdvisory()
    {
        var correct = InvoiceReviewRules.Evaluate(Values, true, [], 2025, 1, new(2026, 11, 13));
        Assert.Equal("no_issue_detected", correct.Single(x => x.Rule == "total.consistency").Result);
        var mismatch = InvoiceReviewRules.Evaluate(Values with { InvoiceTotal = "1250.00" }, true, [], 2025, 1, new(2026, 11, 13));
        var finding = mismatch.Single(x => x.Rule == "total.consistency");
        Assert.Equal("needs_attention", finding.Result);
        Assert.Contains("1150.00", finding.Message);
        Assert.Contains("Possible", finding.Message);
    }
    [Theory]
    [InlineData("", "150.00", "0", true)]
    [InlineData("1000.00", "", "0", true)]
    [InlineData("1000.00", "150.00", "", true)]
    [InlineData("1000.00", "150.00", "0", false)]
    public void MissingOrUnqualifiedFiguresCannotPassCalculation(string subtotal, string vat, string adjustment, bool qualified)
    {
        var findings = InvoiceReviewRules.Evaluate(Values with { Subtotal = subtotal, VatAmount = vat, Adjustments = adjustment, AllAdjustmentsAccountedFor = qualified }, true, [], 2025, 1, new(2026, 11, 13));
        Assert.Equal("unable_to_verify", findings.Single(x => x.Rule == "total.consistency").Result);
    }
    [Fact] public void SignedAdjustmentsAndCentToleranceUseDecimalArithmetic()
    {
        var discounted = Values with { Adjustments = "-50.00", InvoiceTotal = "1100.01" };
        Assert.Equal("no_issue_detected", InvoiceReviewRules.Evaluate(discounted, true, [], 2025, 1, new(2026, 11, 13)).Single(x => x.Rule == "total.consistency").Result);
        Assert.Equal("needs_attention", InvoiceReviewRules.Evaluate(discounted with { InvoiceTotal = "1100.02" }, true, [], 2025, 1, new(2026, 11, 13)).Single(x => x.Rule == "total.consistency").Result);
    }
    [Fact] public void PeriodMismatchAndFutureDateAreWarnings_AndCurrencyIsExplicit()
    {
        var findings = InvoiceReviewRules.Evaluate(Values with { InvoiceDate = "2027-01-01", Currency = "USD" }, true, [], 2025, 1, new(2026, 11, 13));
        Assert.Equal("needs_attention", findings.Single(x => x.Rule == "date.future").Result);
        Assert.Equal("needs_attention", findings.Single(x => x.Rule == "date.period").Result);
        Assert.Equal("unable_to_verify", findings.Single(x => x.Rule == "total.consistency").Result);
    }
    [Theory]
    [InlineData("NaN")]
    [InlineData("1,150.00")]
    [InlineData("0.001")]
    [InlineData("9999999999999999999999999")]
    public void InvalidAmountsAreRejected(string total) => Assert.Throws<AppValidationException>(() => InvoiceReviewRules.Validate(Values with { InvoiceTotal = total }));

    [Fact] public async Task SaveCreatesImmutableVersionedHistory_AndIsIdempotent()
    {
        await using var db = Db(); var document = Seed(db); var service = new InvoiceReviewService(db); var actor = Actor();
        var request = Request(); var saved = await service.SaveAsync(document.Id, request, actor, Ct);
        Assert.Equal(1, saved.Revision); Assert.True(saved.CanAccept); Assert.Equal("uploaded", document.Status);
        var retry = await service.SaveAsync(document.Id, request, actor, Ct);
        Assert.Equal(1, retry.Revision); Assert.Single(db.Set<InvoiceReviewSnapshot>());
        var second = await service.SaveAsync(document.Id, Request(revision: 1, values: Values with { InvoiceNumber = "CORRECTED" }), actor, Ct);
        Assert.Equal(2, second.History.Length);
        Assert.Equal("INV-001", second.History[1].Values.InvoiceNumber);
        Assert.Single(db.AuditLogs.Where(x => x.Action == "documents.invoice_review_saved" && x.MetadataJson!.Contains("\"revision\":1")));
    }
    [Fact] public async Task FindingsNeedOriginalConfirmation_AndExplicitExplanation()
    {
        await using var db = Db(); var document = Seed(db); var service = new InvoiceReviewService(db); var actor = Actor();
        var review = await service.SaveAsync(document.Id, Request(values: Values with { InvoiceTotal = "1250.00" }, confirmed: false), actor, Ct);
        Assert.False(review.CanAccept);
        await Assert.ThrowsAsync<AppValidationException>(() => InvoiceReviewService.EnsureAcceptanceAsync(db, document, new(1, 1), Ct));
        review = await service.SaveAsync(document.Id, Request(revision: 1, values: Values with { InvoiceTotal = "1250.00" }, note: "Verified delivery charge on original"), actor, Ct);
        Assert.True(review.CanAccept);
        Assert.Equal("needs_attention", review.Findings.Single(x => x.Rule == "total.consistency").Result);
        await InvoiceReviewService.EnsureAcceptanceAsync(db, document, new(1, 2), Ct);
    }
    [Fact] public async Task NewFileVersionCannotReusePriorReviewOrApproval()
    {
        await using var db = Db(); var document = Seed(db); var service = new InvoiceReviewService(db); var actor = Actor();
        await service.SaveAsync(document.Id, Request(), actor, Ct);
        document.ReplaceUpload(document.MonthlyPackId, document.Name, document.Category, null, document.FileType, 30, "new-key", Guid.NewGuid());
        await db.SaveChangesAsync(Ct);
        var current = await service.GetAsync(document.Id, actor, Ct);
        Assert.Equal("not_started", current.State); Assert.False(current.CanAccept); Assert.Single(current.History);
        await Assert.ThrowsAsync<AppValidationException>(() => InvoiceReviewService.EnsureAcceptanceAsync(db, document, new(1, 1), Ct));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => service.SaveAsync(document.Id, Request(revision: 1), actor, Ct));
    }
    [Fact] public async Task OtherClientsAndClientUsersCannotReadPrivateFindings()
    {
        await using var db = Db(); var document = Seed(db); var service = new InvoiceReviewService(db);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetAsync(document.Id, Actor("client"), Ct));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetAsync(document.Id, Actor("accountant", Guid.NewGuid()), Ct));
    }
    [Fact] public async Task DuplicateContentInvalidatesPreviouslyAcknowledgedFindings()
    {
        await using var db = Db(); var document = Seed(db); document.SetContentSha256("HASH"); await db.SaveChangesAsync(Ct);
        var service = new InvoiceReviewService(db); var actor = Actor();
        var initial = await service.SaveAsync(document.Id, Request(), actor, Ct); Assert.True(initial.CanAccept);
        var duplicate = Document.CreateUploaded(Guid.NewGuid(), ClientId, document.MonthlyPackId, "duplicate.jpg", "invoices", null, "image/jpeg", 20, "other-key", Guid.NewGuid());
        duplicate.SetContentSha256("HASH"); db.Documents.Add(duplicate); await db.SaveChangesAsync(Ct);
        var updated = await service.GetAsync(document.Id, actor, Ct);
        Assert.True(updated.IsStale); Assert.False(updated.CanAccept);
        Assert.Contains(updated.Findings, x => x.Rule == "possible_duplicate" && x.RelatedDocumentId == duplicate.Id);
    }
    [Fact] public async Task OriginalSuggestionsRemainAvailableAfterAnAccountantCorrection()
    {
        await using var db = Db(); var document = Seed(db); var service = new InvoiceReviewService(db); var actor = Actor();
        var request = Request() with { Source = "on_device_ocr", Suggestions = Values with { InvoiceTotal = "R 1,250.00" } };
        var saved = await service.SaveAsync(document.Id, request, actor, Ct);
        Assert.Equal("1150.00", saved.Values.InvoiceTotal);
        Assert.Equal("R 1,250.00", saved.Suggestions!.InvoiceTotal);
        var reloaded = await service.GetAsync(document.Id, actor, Ct);
        Assert.Equal("R 1,250.00", reloaded.Suggestions!.InvoiceTotal);
    }

    [Fact] public async Task ReclassifyingAMislabelledDocumentPreservesOrdinaryManualReview()
    {
        await using var db = Db(); var document = Seed(db); var service = new InvoiceReviewService(db); var actor = Actor();
        await service.SaveAsync(document.Id, Request(), actor, Ct);
        document.UpdateMetadata(document.Name, "bank_statement", SecureClientPortal.Backend.Domain.Shared.Modules.Documents.DocumentStatus.Uploaded, document.SizeBytes, document.StorageKey);
        await db.SaveChangesAsync(Ct);
        await InvoiceReviewService.EnsureAcceptanceAsync(db, document, null, Ct);
        Assert.Single(db.Set<InvoiceReviewSnapshot>());
    }

    [Fact] public async Task SourceCoordinatesAreValidated_AndAcceptedReviewsCannotBeEdited()
    {
        await using var db = Db(); var document = Seed(db); var service = new InvoiceReviewService(db); var actor = Actor();
        var invalid = Request() with { Evidence = [new("invoiceTotal", "total", .9, .1, .5, .1, 90)] };
        await Assert.ThrowsAsync<AppValidationException>(() => service.SaveAsync(document.Id, invalid, actor, Ct));
        await service.SaveAsync(document.Id, Request(), actor, Ct); document.Accept(); await db.SaveChangesAsync(Ct);
        await Assert.ThrowsAsync<AppValidationException>(() => service.SaveAsync(document.Id, Request(revision: 1), actor, Ct));
    }

    [Fact] public async Task SourcePagesAndConfirmedLinesPersistInCurrentReviewAndHistory()
    {
        await using var db = Db(); var document = Seed(db); var service = new InvoiceReviewService(db);
        var request = Request() with { Evidence = [new("invoiceTotal", "Invoice total 1150.00", .1, .2, .3, .04, null, 1)],
            LineItems = [new("Service", "2", "500.00", "0", "1000.00")], LineItemsConfirmed = true };
        var saved = await service.SaveAsync(document.Id, request, Actor(), Ct);
        Assert.Equal(1, Assert.Single(saved.Evidence).PageIndex);
        Assert.True(saved.LineItemsConfirmed); Assert.Equal("1000.00", Assert.Single(saved.LineItems!).NetTotal);
        Assert.True(Assert.Single(saved.History).LineItemsConfirmed);
        Assert.Equal("no_issue_detected", saved.Findings.Single(x => x.Rule == "lines.subtotal").Result);
        document.MarkUnderReview(); await db.SaveChangesAsync(Ct);
        var queue = await new SecureClientPortal.Backend.Infrastructure.Modules.ReviewQueue.ReviewQueueService(db).GetPendingAsync(Actor(), ct: Ct);
        Assert.Equal("no_issue_detected", Assert.Single(queue.items).AssistantState);
        Assert.Equal(0, Assert.Single(queue.items).AssistantAttentionCount);
    }
    [Theory] [InlineData(-1)] [InlineData(20)]
    public async Task InvalidSourcePagesCannotBeSaved(int page)
    {
        await using var db = Db(); var document = Seed(db);
        var request = Request() with { Evidence = [new("invoiceTotal", "Total", .1, .2, .3, .04, null, page)] };
        await Assert.ThrowsAsync<AppValidationException>(() => new InvoiceReviewService(db).SaveAsync(document.Id, request, Actor(), Ct));
        Assert.Empty(db.Set<InvoiceReviewSnapshot>());
    }
}
