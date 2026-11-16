using System.Data;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts.Modules.Documents;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Domain.Modules.Documents;
using SecureClientPortal.Backend.Models;

namespace SecureClientPortal.Backend.Infrastructure.Modules.Documents.Application;

public sealed class InvoiceReviewService(PortalDbContext db)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<InvoiceReviewResponse> GetAsync(Guid documentId, ClaimsPrincipal actor, CancellationToken ct)
    {
        var document = await FindDocumentAsync(documentId, actor, ct);
        return await BuildResponseAsync(document, ct);
    }

    public async Task<InvoiceReviewResponse> SaveAsync(Guid documentId, SaveInvoiceReviewRequest request, ClaimsPrincipal actor, CancellationToken ct)
    {
        var document = await FindDocumentAsync(documentId, actor, ct);
        if (!InvoiceReviewRules.IsInvoice(document.Category))
            throw new AppValidationException("Invoice checks are available for invoice document types.");
        if (request.RequestId == Guid.Empty || request.Values is null || request.Evidence is null || request.Evidence.Length > 100)
            throw new AppValidationException("Provide a valid review request with at most 100 source regions.");
        var values = InvoiceReviewRules.Validate(request.Values);
        ValidateEvidence(request.Evidence);
        InvoiceReviewLineRules.Validate(request.LineItems);
        // Original OCR output may contain the very mistakes the accountant corrects.
        // Bound its size while validating the corrected values separately.
        if (request.Suggestions is not null && JsonSerializer.Serialize(request.Suggestions, Json).Length > 5000)
            throw new AppValidationException("Original OCR suggestions exceed the storage limit.");
        var note = request.ReviewNote?.Trim();
        if (note?.Length > 2000 || request.Source is not ("manual" or "on_device_ocr"))
            throw new AppValidationException("The review source or note is invalid.");
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, Json))));
        var previous = await db.Set<InvoiceReviewSnapshot>().AsNoTracking().SingleOrDefaultAsync(x => x.DocumentId == documentId && x.RequestId == request.RequestId, ct);
        if (previous is not null)
        {
            if (previous.RequestFingerprint != fingerprint || previous.ActorUserId != actor.GetUserId())
                throw new DbUpdateConcurrencyException("This request id was already used for different review details.");
            return await BuildResponseAsync(document, ct);
        }
        if (document.Status is "accepted" or "filed")
            throw new AppValidationException("Accepted and filed documents retain their review history. Use the normal correction and re-upload workflow for a new version.");
        if (request.ExpectedVersionNumber != document.CurrentVersionNumber || request.ExpectedRevision != document.InvoiceReviewRevision)
            throw new DbUpdateConcurrencyException("This invoice changed. Refresh before saving your review.");
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct) : null;
        var pack = await db.MonthlyPacks.AsNoTracking().SingleAsync(x => x.Id == document.MonthlyPackId && x.ClientId == document.ClientId, ct);
        var input = request with { Values = values, ReviewNote = note };
        var findings = await EvaluateAsync(document, input, pack.Year, pack.Month, ct);
        var versionId = await db.DocumentVersions.Where(x => x.DocumentId == document.Id && x.VersionNumber == document.CurrentVersionNumber)
            .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
        document.RecordInvoiceReview();
        // Lock the document revision before inserting dependent snapshots. The transaction
        // rolls this reservation back together with the snapshot and its audit record.
        await db.SaveChangesAsync(ct);
        var snapshot = new InvoiceReviewSnapshot {
            Id = Guid.NewGuid(), DocumentId = document.Id, DocumentVersionId = versionId,
            VersionNumber = document.CurrentVersionNumber, Revision = document.InvoiceReviewRevision,
            RequestId = request.RequestId, RequestFingerprint = fingerprint, RuleSetVersion = InvoiceReviewRules.Version,
            Year = pack.Year, Month = pack.Month, InputJson = JsonSerializer.Serialize(input, Json),
            FindingsJson = JsonSerializer.Serialize(findings, Json), ActorUserId = actor.GetUserId()!.Value
        };
        db.Add(snapshot);
        db.AuditLogs.Add(AuditLog.Create(Guid.NewGuid(), actor.GetUserId(), actor.IsAdmin() ? "admin" : "accountant",
            "documents.invoice_review_saved", "document", document.Id, document.ClientId,
            JsonSerializer.Serialize(new { snapshot.Id, snapshot.Revision, snapshot.VersionNumber, snapshot.RuleSetVersion, input.Source, input.OriginalConfirmed }, Json)));
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return await BuildResponseAsync(document, ct);
    }

    public static async Task EnsureAcceptanceAsync(PortalDbContext db, Document document, InvoiceReviewContext? context, CancellationToken ct)
    {
        if (!InvoiceReviewRules.IsInvoice(document.Category)) return;
        if (!document.InvoiceReviewRequired && context is null) return;
        if (context is null || context.ExpectedVersionNumber != document.CurrentVersionNumber || context.ExpectedRevision != document.InvoiceReviewRevision)
            throw new AppValidationException("Open the invoice review assistant, save your check against the original, and accept the current version from that workspace.");
        var service = new InvoiceReviewService(db);
        var response = await service.BuildResponseAsync(document, ct);
        if (!response.CanAccept)
            throw new AppValidationException("Save the current invoice review, confirm the original and explain any checks that need manual verification before accepting.");
    }

    public static async Task EnsureFilingAsync(PortalDbContext db, Document document, CancellationToken ct)
    {
        if (!document.InvoiceReviewRequired) return;
        var accepted = await db.ReviewDecisions.AnyAsync(x => x.DocumentId == document.Id && x.Decision == "accepted"
            && x.DocumentVersionNumber == document.CurrentVersionNumber && x.InvoiceReviewRevision == document.InvoiceReviewRevision, ct);
        if (!accepted) throw new AppValidationException("The current invoice version needs an accountant's accepted review before filing.");
    }

    private async Task<Document> FindDocumentAsync(Guid documentId, ClaimsPrincipal actor, CancellationToken ct)
    {
        if ((!actor.IsAdmin() && !actor.IsAccountant()) || actor.GetUserId() is null)
            throw new UnauthorizedAccessException("Accountant access is required.");
        var clients = await actor.GetAccessibleClientIdsAsync(db, ct);
        return await db.Documents.SingleOrDefaultAsync(x => x.Id == documentId && clients.Contains(x.ClientId), ct)
            ?? throw new KeyNotFoundException("Document not found.");
    }

    private async Task<InvoiceReviewResponse> BuildResponseAsync(Document document, CancellationToken ct)
    {
        var pack = await db.MonthlyPacks.AsNoTracking().SingleAsync(x => x.Id == document.MonthlyPackId && x.ClientId == document.ClientId, ct);
        var snapshots = await db.Set<InvoiceReviewSnapshot>().AsNoTracking().Where(x => x.DocumentId == document.Id)
            .OrderByDescending(x => x.Revision).Take(20).ToListAsync(ct);
        var current = snapshots.FirstOrDefault(x => x.VersionNumber == document.CurrentVersionNumber && x.Revision == document.InvoiceReviewRevision);
        var input = current is null ? InitialInput(document) : JsonSerializer.Deserialize<SaveInvoiceReviewRequest>(current.InputJson, Json)!;
        var findings = await EvaluateAsync(document, input, pack.Year, pack.Month, ct);
        var stored = current is null ? [] : JsonSerializer.Deserialize<InvoiceReviewFinding[]>(current.FindingsJson, Json)!;
        var stale = current is not null && (current.RuleSetVersion != InvoiceReviewRules.Version || current.Year != pack.Year || current.Month != pack.Month
            || !stored.Select(x => x.Id).SequenceEqual(findings.Select(x => x.Id)));
        var canAccept = current is not null && !stale && input.OriginalConfirmed
            && (findings.All(x => x.Result == "no_issue_detected") || input.ReviewNote?.Length >= 5)
            && document.Status is "uploaded" or "under_review";
        var history = snapshots.Select(x => {
            var saved = JsonSerializer.Deserialize<SaveInvoiceReviewRequest>(x.InputJson, Json)!;
            return new InvoiceReviewHistory(x.Id, x.VersionNumber, x.Revision, x.ActorUserId, x.CreatedAtUtc, saved.Source, saved.ReviewNote, saved.Values, saved.LineItems, saved.LineItemsConfirmed);
        }).ToArray();
        string? legacyAmount = current is null && document.InvoiceDetailsJson is not null && InvoiceScanDetails.TryParse(document.InvoiceDetailsJson, out var legacy) ? legacy!.Amount : null;
        return new(document.Id, document.CurrentVersionNumber, document.InvoiceReviewRevision,
            current is null ? "not_started" : "completed", stale, canAccept, pack.Year, pack.Month, InvoiceReviewRules.Version,
            input.Values, input.Evidence, input.OriginalConfirmed, input.ReviewNote, findings, history, legacyAmount, input.Suggestions, input.LineItems, input.LineItemsConfirmed);
    }

    private async Task<InvoiceReviewFinding[]> EvaluateAsync(Document document, SaveInvoiceReviewRequest input, int year, int month, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(2));
        var findings = InvoiceReviewRules.Evaluate(input.Values, input.OriginalConfirmed, input.Evidence, year, month, today).ToList();
        findings.AddRange(InvoiceReviewLineRules.Evaluate(input.Values, input.LineItems, input.OriginalConfirmed && input.LineItemsConfirmed));
        if (!InvoiceReviewRules.IsInvoice(document.Category))
            return [new("unsupported", "document.type", "unable_to_verify", "Invoice checks do not cover this document type.", [])];
        var candidates = await db.Documents.AsNoTracking().Where(x => x.ClientId == document.ClientId && x.Id != document.Id
            && (x.InvoiceDetailsJson != null || document.ContentSha256 != null && x.ContentSha256 == document.ContentSha256))
            .Select(x => new { x.Id, x.Name, x.ContentSha256, x.InvoiceDetailsJson }).OrderBy(x => x.Id).ToListAsync(ct);
        var reviewedInputs = await (from candidate in db.Documents.AsNoTracking()
            join snapshot in db.Set<InvoiceReviewSnapshot>().AsNoTracking() on new { DocumentId = candidate.Id, Revision = candidate.InvoiceReviewRevision }
                equals new { snapshot.DocumentId, snapshot.Revision }
            where candidate.ClientId == document.ClientId && candidate.Id != document.Id && snapshot.VersionNumber == candidate.CurrentVersionNumber
            select new { candidate.Id, candidate.Name, snapshot.InputJson }).OrderBy(x => x.Id).ToListAsync(ct);
        var currentValues = reviewedInputs.ToDictionary(x => x.Id, x => JsonSerializer.Deserialize<SaveInvoiceReviewRequest>(x.InputJson, Json)!.Values);
        var comparedIds = new HashSet<Guid>();
        foreach (var candidate in candidates)
        {
            var exact = document.ContentSha256 is not null && candidate.ContentSha256 == document.ContentSha256;
            InvoiceReviewValues? candidateValues = currentValues.GetValueOrDefault(candidate.Id);
            if (candidateValues is null && candidate.InvoiceDetailsJson is not null && InvoiceScanDetails.TryParse(candidate.InvoiceDetailsJson, out var other))
                candidateValues = InvoiceReviewRules.Empty with { SupplierName = other!.SupplierName, InvoiceNumber = other.InvoiceNumber, InvoiceDate = other.InvoiceDate, InvoiceTotal = other.Amount };
            var matches = candidateValues is not null && MatchingFields(input.Values, candidateValues) >= 2;
            comparedIds.Add(candidate.Id);
            if (exact || matches)
            {
                var message = exact ? "Another file for this client has identical content. Check whether this upload is needed."
                    : "Another invoice for this client has two or more matching details. Check whether it is a separate invoice.";
                findings.Add(new(InvoiceReviewRules.FindingId("duplicate", candidate.Id + message), "possible_duplicate", "needs_attention", message,
                    ["supplierName", "invoiceNumber", "invoiceDate", "invoiceTotal"], candidate.Id, candidate.Name));
            }
        }
        foreach (var candidate in reviewedInputs.Where(x => !comparedIds.Contains(x.Id)))
        {
            if (MatchingFields(input.Values, currentValues[candidate.Id]) < 2) continue;
            const string message = "Another reviewed invoice for this client has two or more matching details. Check whether it is separate.";
            findings.Add(new(InvoiceReviewRules.FindingId("duplicate", candidate.Id + message), "possible_duplicate", "needs_attention", message,
                ["supplierName", "invoiceNumber", "invoiceDate", "invoiceTotal"], candidate.Id, candidate.Name));
        }
        if (!findings.Any(x => x.Rule == "possible_duplicate"))
            findings.Add(new("duplicate.none", "possible_duplicate", "no_issue_detected", "No matching invoice was found among this client's records with usable metadata or a matching file fingerprint.", []));
        return findings.ToArray();
    }

    private static int MatchingFields(InvoiceReviewValues left, InvoiceReviewValues right)
    {
        static string Key(string text) => System.Text.RegularExpressions.Regex.Replace(text.ToLowerInvariant(), "[^a-z0-9]", "");
        var count = 0;
        if (Key(left.SupplierName).Length > 0 && Key(left.SupplierName) == Key(right.SupplierName)) count++;
        if (Key(left.InvoiceNumber).Length > 0 && Key(left.InvoiceNumber) == Key(right.InvoiceNumber)) count++;
        if (left.InvoiceDate.Length > 0 && left.InvoiceDate == right.InvoiceDate) count++;
        if (InvoiceReviewRules.TryMoney(left.InvoiceTotal, out var a) && InvoiceReviewRules.TryMoney(right.InvoiceTotal, out var b) && a == b) count++;
        return count;
    }

    private static SaveInvoiceReviewRequest InitialInput(Document document)
    {
        var values = InvoiceReviewRules.Empty;
        if (document.InvoiceDetailsJson is not null && InvoiceScanDetails.TryParse(document.InvoiceDetailsJson, out var legacy))
            values = values with { SupplierName = legacy!.SupplierName, InvoiceNumber = legacy.InvoiceNumber, InvoiceDate = legacy.InvoiceDate };
        // Old Amount may mean invoice total or amount due. Keep it separate until the accountant identifies it.
        return new(Guid.Empty, document.CurrentVersionNumber, document.InvoiceReviewRevision, values, [], false, null, "manual");
    }

    private static void ValidateEvidence(IEnumerable<InvoiceReviewEvidence> evidence)
    {
        foreach (var region in evidence)
        {
            if (region is null || region.PageIndex is < 0 or > 19 || !InvoiceReviewRules.FieldNames.Contains(region.Field) || region.Text is null || region.Text.Length > 500
                || !double.IsFinite(region.X) || !double.IsFinite(region.Y) || !double.IsFinite(region.Width) || !double.IsFinite(region.Height)
                || region.X < 0 || region.Y < 0 || region.Width <= 0 || region.Height <= 0 || region.X + region.Width > 1.000001 || region.Y + region.Height > 1.000001
                || region.Confidence.HasValue && (!double.IsFinite(region.Confidence.Value) || region.Confidence is < 0 or > 100))
                throw new AppValidationException("Source highlight coordinates or text are invalid.");
        }
    }
}
