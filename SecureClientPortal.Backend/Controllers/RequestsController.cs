using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;

namespace SecureClientPortal.Backend.Controllers;

public record CreateRequestRequest(
    string ClientId,
    string RequestType,
    string Title,
    string Description,
    string Priority,
    DateTime? DueDateUtc,
    string? RelatedDocumentId);

public record AddRequestCommentRequest(string Message);
public record ResolveRequestRequest(string? ResolutionNote);

[ApiController]
[Route("api/requests")]
[Authorize(Policy = "ClientOrAccountant")]
public class RequestsController : ControllerBase
{
    private static readonly HashSet<string> AllowedRequestTypes =
    [
        "missing_document",
        "reupload",
        "clarification",
        "renewal",
        "signature"
    ];

    private readonly PortalDbContext _db;

    public RequestsController(PortalDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<RequestItem>>> GetAll()
    {
        var allowedClientIds = await User.GetAccessibleClientIdsAsync(_db);
        return Ok(await _db.Requests
            .Where(x => allowedClientIds.Contains(x.ClientId))
            .OrderByDescending(x => x.RequestedAtUtc)
            .ToListAsync());
    }

    [HttpPost]
    public async Task<ActionResult<RequestItem>> Create([FromBody] CreateRequestRequest request)
    {
        var allowedClientIds = await User.GetAccessibleClientIdsAsync(_db);
        if (!allowedClientIds.Contains(request.ClientId))
        {
            return Forbid();
        }

        var requestType = NormalizeRequestType(request.RequestType);
        if (!AllowedRequestTypes.Contains(requestType))
        {
            return BadRequest(new { error = "Unsupported request type." });
        }

        if (!IsAllowedPriority(request.Priority))
        {
            return BadRequest(new { error = "Priority must be low, medium, high, or urgent." });
        }

        if (!string.IsNullOrWhiteSpace(request.RelatedDocumentId))
        {
            var document = await _db.Documents.FirstOrDefaultAsync(x => x.Id == request.RelatedDocumentId);
            if (document is null || !string.Equals(document.ClientId, request.ClientId, StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { error = "Related document was not found for the selected client." });
            }
        }

        var authorId = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? "unknown";
        var authorRole = User.IsAdmin() ? "admin" : User.IsAccountant() ? "accountant" : "client";
        var status = authorRole == "client" ? "awaiting_accountant" : "awaiting_client";

        var item = new RequestItem
        {
            Id = $"req_{Guid.NewGuid():N}",
            ClientId = request.ClientId,
            RequestType = requestType,
            RelatedDocumentId = request.RelatedDocumentId,
            Title = request.Title.Trim(),
            Description = request.Description.Trim(),
            Priority = request.Priority.Trim().ToLowerInvariant(),
            Status = status,
            DueDateUtc = request.DueDateUtc,
            RequestedByUserId = authorId,
            RequestedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _db.Requests.Add(item);
        await _db.SaveChangesAsync();
        await _db.WriteAuditLogAsync(
            User,
            "request.created",
            "request",
            item.Id,
            item.ClientId,
            JsonSerializer.Serialize(new { item.RequestType, item.Priority, item.Status, item.RelatedDocumentId }));

        var notificationAudience = authorRole == "client" ? "accountant" : "client";
        var recipients = await _db.ResolveNotificationRecipientsAsync(item.ClientId, notificationAudience);
        await _db.AddNotificationsAsync(
            User,
            recipients,
            item.ClientId,
            "request.created",
            "New workflow request",
            $"A {item.RequestType.Replace('_', ' ')} request was created for '{item.Title}'.",
            $"/requests/{item.Id}",
            new { item.Id, item.RequestType });

        return CreatedAtAction(nameof(GetById), new { id = item.Id }, item);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<RequestItem>> GetById(string id)
    {
        var item = await _db.Requests.FindAsync(id);
        if (item is null) return NotFound();
        var allowedClientIds = await User.GetAccessibleClientIdsAsync(_db);
        if (!allowedClientIds.Contains(item.ClientId))
        {
            return Forbid();
        }

        return Ok(item);
    }

    [HttpPut("{id}")]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<ActionResult<RequestItem>> Update(string id, [FromBody] RequestItem request)
    {
        var item = await _db.Requests.FindAsync(id);
        if (item is null) return NotFound();
        var allowedClientIds = await User.GetAccessibleClientIdsAsync(_db);
        if (!allowedClientIds.Contains(item.ClientId))
        {
            return Forbid();
        }

        item.Title = request.Title;
        item.Description = request.Description;
        item.Priority = request.Priority;
        item.Status = request.Status;
        item.DueDateUtc = request.DueDateUtc;
        item.RequestType = NormalizeRequestType(request.RequestType);
        item.RelatedDocumentId = request.RelatedDocumentId;
        item.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        await _db.WriteAuditLogAsync(
            User,
            "request.updated",
            "request",
            item.Id,
            item.ClientId,
            JsonSerializer.Serialize(new { item.Status, item.Priority, item.RequestType }));
        return Ok(item);
    }

    [HttpGet("{id}/comments")]
    public async Task<ActionResult<IEnumerable<RequestComment>>> GetComments(string id)
    {
        var item = await _db.Requests.FindAsync(id);
        if (item is null) return NotFound();

        var allowedClientIds = await User.GetAccessibleClientIdsAsync(_db);
        if (!allowedClientIds.Contains(item.ClientId))
        {
            return Forbid();
        }

        var comments = await _db.RequestComments
            .Where(x => x.RequestId == item.Id)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync();
        return Ok(comments);
    }

    [HttpPost("{id}/comments")]
    public async Task<ActionResult<RequestComment>> AddComment(string id, [FromBody] AddRequestCommentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return BadRequest(new { error = "Comment message is required." });
        }

        var item = await _db.Requests.FindAsync(id);
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
        var comment = new RequestComment
        {
            Id = $"rc_{Guid.NewGuid():N}",
            RequestId = item.Id,
            ClientId = item.ClientId,
            AuthorUserId = authorId,
            AuthorRole = authorRole,
            Message = request.Message.Trim(),
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.RequestComments.Add(comment);

        item.Status = authorRole == "client" ? "awaiting_accountant" : "awaiting_client";
        item.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        await _db.WriteAuditLogAsync(
            User,
            "comment.added",
            "request",
            item.Id,
            item.ClientId,
            JsonSerializer.Serialize(new { comment.Id, comment.AuthorRole, target = "request" }));

        var recipientRole = authorRole == "client" ? "accountant" : "client";
        var recipientIds = await _db.ResolveNotificationRecipientsAsync(item.ClientId, recipientRole);
        await _db.AddNotificationsAsync(
            User,
            recipientIds,
            item.ClientId,
            "request.replied",
            "Request replied to",
            $"New comment on request '{item.Title}'.",
            $"/requests/{item.Id}",
            new { requestId = item.Id, commentId = comment.Id });

        return Ok(comment);
    }

    [HttpPost("{id}/resolve")]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<ActionResult<RequestItem>> Resolve(string id, [FromBody] ResolveRequestRequest request)
    {
        var item = await _db.Requests.FindAsync(id);
        if (item is null)
        {
            return NotFound();
        }

        var allowedClientIds = await User.GetAccessibleClientIdsAsync(_db);
        if (!allowedClientIds.Contains(item.ClientId))
        {
            return Forbid();
        }

        item.Status = "resolved";
        item.ResolvedByUserId = User.GetUserId();
        item.ResolvedAtUtc = DateTime.UtcNow;
        item.UpdatedAtUtc = item.ResolvedAtUtc.Value;
        await _db.SaveChangesAsync();

        await _db.WriteAuditLogAsync(
            User,
            "request.resolved",
            "request",
            item.Id,
            item.ClientId,
            JsonSerializer.Serialize(new { item.RequestType, request.ResolutionNote, item.RelatedDocumentId }));

        var recipients = await _db.ResolveNotificationRecipientsAsync(item.ClientId, "client");
        await _db.AddNotificationsAsync(
            User,
            recipients,
            item.ClientId,
            "request.resolved",
            "Request resolved",
            $"Request '{item.Title}' has been resolved.",
            $"/requests/{item.Id}",
            new { requestId = item.Id, request.ResolutionNote });

        return Ok(item);
    }

    [HttpDelete("{id}")]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<IActionResult> Delete(string id)
    {
        var item = await _db.Requests.FindAsync(id);
        if (item is null) return NotFound();
        var allowedClientIds = await User.GetAccessibleClientIdsAsync(_db);
        if (!allowedClientIds.Contains(item.ClientId))
        {
            return Forbid();
        }

        _db.Requests.Remove(item);
        await _db.SaveChangesAsync();
        await _db.WriteAuditLogAsync(User, "request.deleted", "request", item.Id, item.ClientId);
        return NoContent();
    }

    private static string NormalizeRequestType(string value)
    {
        return value.Trim().ToLowerInvariant().Replace("-", "_").Replace(" ", "_");
    }

    private static bool IsAllowedPriority(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized is "low" or "medium" or "high" or "urgent";
    }
}
