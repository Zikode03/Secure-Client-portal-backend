using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Models;
using SecureClientPortal.Backend.Storage;
using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;

namespace SecureClientPortal.Backend.Controllers;

public record UpdateDocumentStatusRequest(string Status);
public record FilingRuleUpdateRequest(bool IsEnabled);
public record AddDocumentCommentRequest(string Message);
public record AddReviewDecisionRequest(string Decision, string? Reason);
public record RequestReuploadRequest(string Reason);

public class UploadDocumentRequest
{
    public string ClientId { get; set; } = string.Empty;
    public string? MonthlyPackId { get; set; }
    public string? DocumentSlotId { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public string? DocumentId { get; set; }
    public IFormFile File { get; set; } = default!;
}

[ApiController]
[Route("api/documents")]
[Authorize(Policy = "ClientOrAccountant")]
public class DocumentsController : ControllerBase
{
    private readonly PortalDbContext _db;
    private readonly IFileStorage _fileStorage;

    public DocumentsController(PortalDbContext db, IFileStorage fileStorage)
    {
        _db = db;
        _fileStorage = fileStorage;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Document>>> GetAll()
    {
        var allowedClientIds = await User.GetAccessibleClientIdsAsync(_db);
        return Ok(await _db.Documents
            .Where(x => allowedClientIds.Contains(x.ClientId))
            .OrderByDescending(x => x.UploadedAtUtc)
            .ToListAsync());
    }

    [HttpGet("filing-register")]
    public async Task<ActionResult<IEnumerable<Document>>> GetFilingRegister([FromQuery] string? clientId = null)
    {
        var allowedClientIds = await User.GetAccessibleClientIdsAsync(_db);
        var query = _db.Documents.Where(x => x.IsFiled && allowedClientIds.Contains(x.ClientId));
        if (!string.IsNullOrWhiteSpace(clientId))
        {
            if (!allowedClientIds.Contains(clientId))
            {
                return Forbid();
            }

            query = query.Where(x => x.ClientId == clientId);
        }

        return Ok(await query.OrderByDescending(x => x.FiledAtUtc).ThenByDescending(x => x.UploadedAtUtc).ToListAsync());
    }

    [HttpGet("filing-rules")]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<ActionResult<IEnumerable<FilingRule>>> GetFilingRules()
    {
        return Ok(await _db.FilingRules.OrderBy(x => x.Category).ToListAsync());
    }

    [HttpPut("filing-rules/{category}")]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<ActionResult<FilingRule>> UpdateFilingRule(string category, [FromBody] FilingRuleUpdateRequest request)
    {
        var normalizedCategory = NormalizeCategory(category);
        var item = await _db.FilingRules.FirstOrDefaultAsync(x => x.Category == normalizedCategory);
        if (item is null) return NotFound();

        item.IsEnabled = request.IsEnabled;
        item.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(item);
    }

    [HttpPost("upload")]
    [RequestSizeLimit(100_000_000)]
    public async Task<ActionResult<object>> Upload([FromForm] UploadDocumentRequest request, CancellationToken ct)
    {
        if (request.File is null || request.File.Length == 0)
        {
            return BadRequest(new { error = "A file is required." });
        }

        var allowedClientIds = await User.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(request.ClientId))
        {
            return Forbid();
        }

        var normalizedCategory = NormalizeCategory(request.DocumentType);
        var pack = await ResolveMonthlyPackAsync(request.ClientId, request.MonthlyPackId, request.DocumentSlotId, ct);
        if (pack is null)
        {
            return BadRequest(new { error = "A valid monthly pack is required for document upload." });
        }

        var slot = await ResolveSlotAsync(request.ClientId, pack.Id, request.DocumentSlotId, normalizedCategory, ct);
        if (slot is null)
        {
            return BadRequest(new { error = "A valid document slot is required for document upload." });
        }

        var stored = await _fileStorage.SaveAsync(request.File, request.ClientId, ct);
        var actorUserId = User.GetUserId() ?? "unknown";
        var now = DateTime.UtcNow;

        Document document;
        var isNewDocument = string.IsNullOrWhiteSpace(request.DocumentId);
        if (isNewDocument)
        {
            document = new Document
            {
                Id = $"doc_{Guid.NewGuid():N}",
                ClientId = request.ClientId,
                Name = stored.OriginalFileName,
                Category = normalizedCategory,
                DocumentSlotId = slot.Id,
                Status = "pending",
                SizeBytes = stored.SizeBytes,
                StorageKey = stored.StorageKey,
                UploadedByUserId = actorUserId,
                CurrentVersionNumber = 1,
                UploadedAtUtc = now,
                UpdatedAtUtc = now
            };
            _db.Documents.Add(document);
        }
        else
        {
            var existingDocument = await _db.Documents.FirstOrDefaultAsync(x => x.Id == request.DocumentId, ct);
            if (existingDocument is null)
            {
                return NotFound(new { error = "Requested document could not be found." });
            }

            document = existingDocument;
            if (!string.Equals(document.ClientId, request.ClientId, StringComparison.OrdinalIgnoreCase))
            {
                return Forbid();
            }

            document.Name = stored.OriginalFileName;
            document.Category = normalizedCategory;
            document.DocumentSlotId = slot.Id;
            document.SizeBytes = stored.SizeBytes;
            document.StorageKey = stored.StorageKey;
            document.UploadedByUserId = actorUserId;
            document.Status = "pending";
            document.CurrentVersionNumber += 1;
            document.IsFiled = false;
            document.FiledAtUtc = null;
            document.FiledByUserId = null;
            document.UpdatedAtUtc = now;
            document.UploadedAtUtc = now;
        }

        _db.DocumentVersions.Add(new DocumentVersion
        {
            Id = $"dv_{Guid.NewGuid():N}",
            DocumentId = document.Id,
            VersionNumber = document.CurrentVersionNumber,
            Name = stored.OriginalFileName,
            SizeBytes = stored.SizeBytes,
            StorageKey = stored.StorageKey,
            UploadedByUserId = actorUserId,
            CreatedAtUtc = now
        });

        slot.CurrentDocumentId = document.Id;
        slot.Status = "uploaded";
        slot.UpdatedAtUtc = now;

        pack.Status = "submitted";
        pack.UpdatedAtUtc = now;

        await _db.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(
            User,
            "documents.uploaded",
            "document",
            document.Id,
            document.ClientId,
            JsonSerializer.Serialize(new
            {
                document.Id,
                document.ClientId,
                monthlyPackId = pack.Id,
                documentSlotId = slot.Id,
                versionNumber = document.CurrentVersionNumber,
                document.StorageKey
            }),
            ct);

        var accountants = await _db.ResolveNotificationRecipientsAsync(document.ClientId, "accountant", ct);
        await _db.AddNotificationsAsync(
            User,
            accountants,
            document.ClientId,
            "monthly_pack.submitted",
            "Monthly pack submitted",
            $"A new document was submitted for review in monthly pack {pack.Year:D4}-{pack.Month:D2}.",
            $"/documents/{document.Id}",
            new { document.Id, monthlyPackId = pack.Id, versionNumber = document.CurrentVersionNumber },
            ct);

        return Created($"/api/documents/{document.Id}", await BuildDocumentResponseAsync(document, ct));
    }

    [HttpPost]
    public async Task<ActionResult<Document>> Create([FromBody] Document request)
    {
        var allowedClientIds = await User.GetAccessibleClientIdsAsync(_db);
        if (!allowedClientIds.Contains(request.ClientId))
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(request.Id)) request.Id = $"doc_{Guid.NewGuid():N}";
        request.Category = NormalizeCategory(request.Category);
        request.Status = NormalizeDocumentStatus(request.Status);
        request.UploadedAtUtc = DateTime.UtcNow;
        request.UpdatedAtUtc = request.UploadedAtUtc;
        request.CurrentVersionNumber = 1;

        _db.Documents.Add(request);
        _db.DocumentVersions.Add(new DocumentVersion
        {
            Id = $"dv_{Guid.NewGuid():N}",
            DocumentId = request.Id,
            VersionNumber = 1,
            Name = request.Name,
            SizeBytes = request.SizeBytes,
            StorageKey = request.StorageKey,
            UploadedByUserId = request.UploadedByUserId,
            CreatedAtUtc = request.UploadedAtUtc
        });

        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id = request.Id }, request);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<object>> GetById(string id, CancellationToken ct)
    {
        var item = await _db.Documents.FindAsync([id], ct);
        if (item is null) return NotFound();
        var allowedClientIds = await User.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(item.ClientId))
        {
            return Forbid();
        }

        return Ok(await BuildDocumentResponseAsync(item, ct));
    }

    [HttpGet("{id}/versions")]
    public async Task<ActionResult<IEnumerable<object>>> GetVersions(string id, CancellationToken ct)
    {
        var item = await _db.Documents.FindAsync([id], ct);
        if (item is null) return NotFound();

        var allowedClientIds = await User.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(item.ClientId))
        {
            return Forbid();
        }

        var versions = await _db.DocumentVersions
            .Where(x => x.DocumentId == id)
            .OrderByDescending(x => x.VersionNumber)
            .ThenByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);

        return Ok(versions.Select(version => new
        {
            version.Id,
            version.DocumentId,
            version.VersionNumber,
            version.Name,
            version.SizeBytes,
            version.StorageKey,
            version.UploadedByUserId,
            version.CreatedAtUtc,
            isCurrent = version.VersionNumber == item.CurrentVersionNumber
        }));
    }

    [HttpGet("{id}/download")]
    public async Task<IActionResult> Download(string id, CancellationToken ct)
    {
        var item = await _db.Documents.FindAsync([id], ct);
        if (item is null) return NotFound();

        var allowedClientIds = await User.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(item.ClientId))
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(item.StorageKey))
        {
            return NotFound(new { error = "Document file is not available." });
        }

        var file = await _fileStorage.OpenReadAsync(item.StorageKey, ct);
        if (file is null)
        {
            return NotFound(new { error = "Document file could not be found in storage." });
        }

        return File(file.Stream, file.ContentType, item.Name);
    }

    [HttpPut("{id}")]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<ActionResult<Document>> Update(string id, [FromBody] Document request)
    {
        var item = await _db.Documents.FindAsync(id);
        if (item is null) return NotFound();
        var allowedClientIds = await User.GetAccessibleClientIdsAsync(_db);
        if (!allowedClientIds.Contains(item.ClientId))
        {
            return Forbid();
        }

        item.Name = request.Name;
        item.Category = NormalizeCategory(request.Category);
        item.Status = NormalizeDocumentStatus(request.Status);
        item.SizeBytes = request.SizeBytes;
        item.StorageKey = request.StorageKey;
        item.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return Ok(item);
    }

    [HttpPut("{id}/status")]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<ActionResult<Document>> UpdateStatus(string id, [FromBody] UpdateDocumentStatusRequest request, CancellationToken ct)
    {
        var document = await _db.Documents.FindAsync([id], ct);
        if (document is null) return NotFound();

        var allowedClientIds = await User.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(document.ClientId))
        {
            return Forbid();
        }

        await ApplyLifecycleDecisionAsync(document, NormalizeDocumentStatus(request.Status), null, ct);
        return Ok(document);
    }

    [HttpPost("{id}/review")]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<ActionResult<object>> Review(string id, [FromBody] AddReviewDecisionRequest request, CancellationToken ct)
    {
        var decision = request.Decision.Trim().ToLowerInvariant();
        if (decision is not ("under_review" or "accepted" or "rejected"))
        {
            return BadRequest(new { error = "Decision must be under_review, accepted, or rejected." });
        }

        var document = await _db.Documents.FindAsync([id], ct);
        if (document is null) return NotFound();

        var allowedClientIds = await User.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(document.ClientId))
        {
            return Forbid();
        }

        var reviewDecision = await ApplyLifecycleDecisionAsync(document, decision, request.Reason, ct);
        return Ok(new
        {
            reviewDecision.Id,
            reviewDecision.Decision,
            reviewDecision.Reason,
            reviewDecision.DecidedAtUtc,
            documentId = document.Id,
            documentStatus = document.Status
        });
    }

    [HttpPost("{id}/request-reupload")]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<ActionResult<object>> RequestReupload(string id, [FromBody] RequestReuploadRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return BadRequest(new { error = "A reason is required when requesting a re-upload." });
        }

        var document = await _db.Documents.FindAsync([id], ct);
        if (document is null) return NotFound();

        var allowedClientIds = await User.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(document.ClientId))
        {
            return Forbid();
        }

        var reviewDecision = await ApplyLifecycleDecisionAsync(document, "request_reupload", request.Reason, ct);
        return Ok(new
        {
            reviewDecision.Id,
            reviewDecision.Decision,
            reviewDecision.Reason,
            reviewDecision.DecidedAtUtc,
            documentId = document.Id,
            documentStatus = document.Status
        });
    }

    [HttpPost("{id}/comments")]
    public async Task<ActionResult<DocumentComment>> AddComment(string id, [FromBody] AddDocumentCommentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return BadRequest(new { error = "Comment message is required." });
        }

        var item = await _db.Documents.FindAsync(id);
        if (item is null) return NotFound();
        var allowedClientIds = await User.GetAccessibleClientIdsAsync(_db);
        if (!allowedClientIds.Contains(item.ClientId))
        {
            return Forbid();
        }

        var authorId = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? "unknown";
        var authorRole = User.IsInRole("admin")
            ? "admin"
            : User.IsInRole("accountant")
                ? "accountant"
                : "client";

        var comment = new DocumentComment
        {
            Id = $"dc_{Guid.NewGuid():N}",
            DocumentId = item.Id,
            AuthorUserId = authorId,
            AuthorRole = authorRole,
            Message = request.Message.Trim(),
            CreatedAtUtc = DateTime.UtcNow,
        };

        _db.DocumentComments.Add(comment);
        await _db.SaveChangesAsync();
        await _db.WriteAuditLogAsync(
            User,
            "comment.added",
            "document",
            item.Id,
            item.ClientId,
            JsonSerializer.Serialize(new { comment.Id, comment.AuthorRole, target = "document" }));

        var recipientRole = authorRole == "client" ? "accountant" : "client";
        var recipients = await _db.ResolveNotificationRecipientsAsync(item.ClientId, recipientRole);
        await _db.AddNotificationsAsync(
            User,
            recipients,
            item.ClientId,
            "document.comment",
            "Document comment added",
            $"New comment added to document '{item.Name}'.",
            $"/documents/{item.Id}",
            new { documentId = item.Id, commentId = comment.Id });
        return Ok(comment);
    }

    [HttpDelete("{id}")]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<IActionResult> Delete(string id)
    {
        var item = await _db.Documents.FindAsync(id);
        if (item is null) return NotFound();
        var allowedClientIds = await User.GetAccessibleClientIdsAsync(_db);
        if (!allowedClientIds.Contains(item.ClientId))
        {
            return Forbid();
        }

        _db.Documents.Remove(item);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private async Task<object> BuildDocumentResponseAsync(Document item, CancellationToken ct)
    {
        DocumentSlot? slot = null;
        MonthlyPack? pack = null;

        if (!string.IsNullOrWhiteSpace(item.DocumentSlotId))
        {
            slot = await _db.DocumentSlots.FirstOrDefaultAsync(x => x.Id == item.DocumentSlotId, ct);
            if (slot is not null)
            {
                pack = await _db.MonthlyPacks.FirstOrDefaultAsync(x => x.Id == slot.MonthlyPackId, ct);
            }
        }

        return new
        {
            item.Id,
            item.ClientId,
            monthlyPackId = pack?.Id,
            documentSlotId = slot?.Id,
            documentType = item.Category,
            item.Name,
            item.Status,
            item.SizeBytes,
            item.StorageKey,
            item.UploadedByUserId,
            item.CurrentVersionNumber,
            item.UploadedAtUtc,
            item.UpdatedAtUtc
        };
    }

    private async Task<MonthlyPack?> ResolveMonthlyPackAsync(string clientId, string? monthlyPackId, string? documentSlotId, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(documentSlotId))
        {
            var slot = await _db.DocumentSlots.FirstOrDefaultAsync(x => x.Id == documentSlotId, ct);
            if (slot is not null)
            {
                return await _db.MonthlyPacks.FirstOrDefaultAsync(x => x.Id == slot.MonthlyPackId && x.ClientId == clientId, ct);
            }
        }

        if (!string.IsNullOrWhiteSpace(monthlyPackId))
        {
            return await _db.MonthlyPacks.FirstOrDefaultAsync(x => x.Id == monthlyPackId && x.ClientId == clientId, ct);
        }

        return null;
    }

    private async Task<DocumentSlot?> ResolveSlotAsync(string clientId, string monthlyPackId, string? documentSlotId, string category, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(documentSlotId))
        {
            return await _db.DocumentSlots.FirstOrDefaultAsync(x =>
                x.Id == documentSlotId && x.ClientId == clientId && x.MonthlyPackId == monthlyPackId, ct);
        }

        return await _db.DocumentSlots.FirstOrDefaultAsync(x =>
            x.ClientId == clientId && x.MonthlyPackId == monthlyPackId && x.Category == category, ct);
    }

    private async Task<ReviewDecision> ApplyLifecycleDecisionAsync(Document document, string decision, string? reason, CancellationToken ct)
    {
        var reviewerUserId = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? "unknown";
        var reviewerRole = User.IsAdmin() ? "admin" : "accountant";
        var now = DateTime.UtcNow;

        var reviewDecision = new ReviewDecision
        {
            Id = $"rd_{Guid.NewGuid():N}",
            DocumentId = document.Id,
            Decision = decision,
            ReviewerUserId = reviewerUserId,
            ReviewerRole = reviewerRole,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            DecidedAtUtc = now
        };

        _db.ReviewDecisions.Add(reviewDecision);

        var slot = string.IsNullOrWhiteSpace(document.DocumentSlotId)
            ? null
            : await _db.DocumentSlots.FirstOrDefaultAsync(x => x.Id == document.DocumentSlotId, ct);
        var pack = slot is null
            ? null
            : await _db.MonthlyPacks.FirstOrDefaultAsync(x => x.Id == slot.MonthlyPackId, ct);

        // Keep the document, slot, and monthly pack in sync so workflow and reporting read one lifecycle state.
        switch (decision)
        {
            case "under_review":
                document.Status = "under_review";
                if (slot is not null)
                {
                    slot.Status = "under_review";
                    slot.UpdatedAtUtc = now;
                }
                if (pack is not null)
                {
                    pack.Status = "under_review";
                    pack.UpdatedAtUtc = now;
                }
                break;

            case "accepted":
                document.Status = "accepted";
                document.IsFiled = false;
                document.FiledAtUtc = null;
                document.FiledByUserId = null;
                if (slot is not null)
                {
                    slot.Status = "accepted";
                    slot.CurrentDocumentId = document.Id;
                    slot.UpdatedAtUtc = now;
                }
                break;

            case "rejected":
            case "request_reupload":
                document.Status = "rejected";
                document.IsFiled = false;
                document.FiledAtUtc = null;
                document.FiledByUserId = null;
                if (slot is not null)
                {
                    slot.Status = "rejected";
                    slot.CurrentDocumentId = document.Id;
                    slot.UpdatedAtUtc = now;
                }
                break;

            default:
                throw new InvalidOperationException("Unsupported document lifecycle decision.");
        }

        document.UpdatedAtUtc = now;

        if (pack is not null)
        {
            await RecalculateMonthlyPackStatusAsync(pack, ct);
        }

        await _db.SaveChangesAsync(ct);

        var action = decision switch
        {
            "under_review" => "documents.reviewed",
            "accepted" => "documents.accepted",
            "rejected" => "documents.rejected",
            "request_reupload" => "documents.reupload_requested",
            _ => "documents.reviewed"
        };

        await _db.WriteAuditLogAsync(
            User,
            action,
            "document",
            document.Id,
            document.ClientId,
            JsonSerializer.Serialize(new
            {
                document.Id,
                decision,
                reason = reviewDecision.Reason,
                document.Status
            }),
            ct);

        if (decision is "rejected" or "request_reupload")
        {
            // A rejected document should become a trackable workflow item, not just a status change.
            await EnsureReuploadRequestAsync(document, reviewDecision.Reason ?? "Please re-upload a corrected document.", ct);
        }

        if (decision is "rejected" or "request_reupload")
        {
            var recipients = await _db.ResolveNotificationRecipientsAsync(document.ClientId, "client", ct);
            await _db.AddNotificationsAsync(
                User,
                recipients,
                document.ClientId,
                decision == "rejected" ? "document.rejected" : "document.reupload_requested",
                decision == "rejected" ? "Document rejected" : "Re-upload requested",
                decision == "rejected"
                    ? $"Document '{document.Name}' was rejected."
                    : $"A corrected version of '{document.Name}' was requested.",
                $"/documents/{document.Id}",
                new { document.Id, reason = reviewDecision.Reason },
                ct);
        }

        return reviewDecision;
    }

    private async Task EnsureReuploadRequestAsync(Document document, string reason, CancellationToken ct)
    {
        var existing = await _db.Requests.FirstOrDefaultAsync(x =>
            x.ClientId == document.ClientId &&
            x.RelatedDocumentId == document.Id &&
            x.RequestType == "reupload" &&
            x.Status != "resolved", ct);

        if (existing is not null)
        {
            existing.Status = "awaiting_client";
            existing.Description = reason.Trim();
            existing.UpdatedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return;
        }

        var requestItem = new RequestItem
        {
            Id = $"req_{Guid.NewGuid():N}",
            ClientId = document.ClientId,
            RequestType = "reupload",
            RelatedDocumentId = document.Id,
            Title = $"Re-upload required: {document.Name}",
            Description = reason.Trim(),
            Priority = "high",
            Status = "awaiting_client",
            RequestedByUserId = User.GetUserId() ?? "unknown",
            RequestedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _db.Requests.Add(requestItem);
        await _db.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(
            User,
            "request.created",
            "request",
            requestItem.Id,
            requestItem.ClientId,
            JsonSerializer.Serialize(new { requestItem.RequestType, requestItem.RelatedDocumentId, requestItem.Status }),
            ct);
    }

    private async Task RecalculateMonthlyPackStatusAsync(MonthlyPack pack, CancellationToken ct)
    {
        var slots = await _db.DocumentSlots.Where(x => x.MonthlyPackId == pack.Id).ToListAsync(ct);
        if (slots.Count == 0)
        {
            pack.Status = "draft";
            pack.UpdatedAtUtc = DateTime.UtcNow;
            return;
        }

        if (slots.Where(x => x.IsRequired).All(x => x.Status == "accepted"))
        {
            pack.Status = "completed";
        }
        else if (slots.Any(x => x.Status == "under_review"))
        {
            pack.Status = "under_review";
        }
        else if (slots.Any(x => x.Status == "uploaded"))
        {
            pack.Status = "submitted";
        }
        else if (slots.Any(x => x.Status is "accepted" or "rejected" or "filed"))
        {
            pack.Status = "in_progress";
        }
        else
        {
            pack.Status = "draft";
        }

        pack.UpdatedAtUtc = DateTime.UtcNow;
    }

    private static string NormalizeCategory(string value)
    {
        var raw = value.Trim().ToLowerInvariant().Replace("-", "_").Replace(" ", "_");
        return raw switch
        {
            "bankstatement" => "bank_statement",
            "bank_statement" => "bank_statement",
            "invoice" => "invoices",
            "invoices" => "invoices",
            "signeddocuments" => "signed_documents",
            "signed_documents" => "signed_documents",
            "compliancerecord" => "compliance_record",
            "compliance_record" => "compliance_record",
            "payrollsummary" => "payroll_summary",
            "payroll_summary" => "payroll_summary",
            "taxworkingpapers" => "tax_working_papers",
            "tax_working_papers" => "tax_working_papers",
            "proofofpayment" => "proof_of_payment",
            "proof_of_payment" => "proof_of_payment",
            "creditnotes" => "credit_notes",
            "credit_notes" => "credit_notes",
            "debitnotes" => "debit_notes",
            "debit_notes" => "debit_notes",
            _ => raw
        };
    }

    private static string NormalizeDocumentStatus(string rawStatus)
    {
        var normalized = string.IsNullOrWhiteSpace(rawStatus) ? "pending" : rawStatus.Trim().ToLowerInvariant();
        return normalized switch
        {
            "draft" => "draft",
            "pending" => "pending",
            "submitted" => "pending",
            "under_review" => "under_review",
            "accepted" => "accepted",
            "rejected" => "rejected",
            "filed" => "filed",
            _ => "pending"
        };
    }
}
