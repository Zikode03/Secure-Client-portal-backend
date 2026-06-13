using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Models;

namespace SecureClientPortal.Backend.Controllers;

public record CreateDocumentSlotRequest(
    string MonthlyPackId,
    string Category,
    string Label,
    bool IsRequired,
    DateTime? DueDateUtc);

[ApiController]
[Route("api/document-slots")]
[Authorize(Policy = "ClientOrAccountant")]
public class DocumentSlotsController : ControllerBase
{
    private readonly PortalDbContext _db;

    public DocumentSlotsController(PortalDbContext db)
    {
        _db = db;
    }

    [HttpGet("{monthlyPackId}")]
    public async Task<ActionResult<IEnumerable<DocumentSlot>>> GetByMonthlyPackId(string monthlyPackId)
    {
        var pack = await _db.MonthlyPacks.FirstOrDefaultAsync(x => x.Id == monthlyPackId);
        if (pack is null)
        {
            return NotFound();
        }

        var allowedClientIds = await User.GetAccessibleClientIdsAsync(_db);
        if (!allowedClientIds.Contains(pack.ClientId))
        {
            return Forbid();
        }

        var slots = await _db.DocumentSlots
            .Where(x => x.MonthlyPackId == monthlyPackId)
            .OrderByDescending(x => x.IsRequired)
            .ThenBy(x => x.Label)
            .ToListAsync();

        return Ok(slots);
    }

    [HttpPost]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<ActionResult<DocumentSlot>> Create([FromBody] CreateDocumentSlotRequest request)
    {
        var pack = await _db.MonthlyPacks.FirstOrDefaultAsync(x => x.Id == request.MonthlyPackId);
        if (pack is null)
        {
            return BadRequest(new { error = "Monthly pack was not found." });
        }

        var allowedClientIds = await User.GetAccessibleClientIdsAsync(_db);
        if (!User.IsAdmin() && !allowedClientIds.Contains(pack.ClientId))
        {
            return Forbid();
        }

        var normalizedCategory = NormalizeCategory(request.Category);
        var existing = await _db.DocumentSlots.FirstOrDefaultAsync(x =>
            x.MonthlyPackId == request.MonthlyPackId && x.Category == normalizedCategory);
        if (existing is not null)
        {
            existing.Label = request.Label.Trim();
            existing.IsRequired = request.IsRequired;
            existing.DueDateUtc = request.DueDateUtc;
            existing.UpdatedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return Ok(existing);
        }

        var slot = new DocumentSlot
        {
            Id = $"slot_{Guid.NewGuid():N}",
            MonthlyPackId = request.MonthlyPackId,
            ClientId = pack.ClientId,
            Category = normalizedCategory,
            Label = request.Label.Trim(),
            IsRequired = request.IsRequired,
            Status = "missing",
            DueDateUtc = request.DueDateUtc,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _db.DocumentSlots.Add(slot);
        await _db.SaveChangesAsync();
        return Created($"/api/document-slots/{slot.MonthlyPackId}", slot);
    }

    private static string NormalizeCategory(string value)
    {
        return value.Trim().ToLowerInvariant().Replace("-", "_").Replace(" ", "_");
    }
}
