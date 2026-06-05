using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Models;
using System.Text.Json;

namespace SecureClientPortal.Backend.Controllers;

public record CreateMonthlyPackRequest(string ClientId, int Year, int Month, string? Status);
public record UpdateMonthlyPackStatusRequest(string Status);

[ApiController]
[Route("api/monthly-packs")]
[Authorize(Policy = "ClientOrAccountant")]
public class MonthlyPacksController : ControllerBase
{
    private readonly PortalDbContext _db;

    public MonthlyPacksController(PortalDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<MonthlyPack>>> GetAll([FromQuery] string? clientId = null)
    {
        var allowedClientIds = await User.GetAccessibleClientIdsAsync(_db);
        var query = _db.MonthlyPacks.Where(x => allowedClientIds.Contains(x.ClientId));
        if (!string.IsNullOrWhiteSpace(clientId))
        {
            if (!allowedClientIds.Contains(clientId))
            {
                return Forbid();
            }
            query = query.Where(x => x.ClientId == clientId);
        }

        return Ok(await query.OrderByDescending(x => x.Year).ThenByDescending(x => x.Month).ToListAsync());
    }

    [HttpGet("{clientId}/{year:int}/{month:int}")]
    public async Task<ActionResult<MonthlyPack>> GetByClientAndPeriod(string clientId, int year, int month)
    {
        var allowedClientIds = await User.GetAccessibleClientIdsAsync(_db);
        if (!allowedClientIds.Contains(clientId))
        {
            return Forbid();
        }

        var pack = await _db.MonthlyPacks.FirstOrDefaultAsync(x =>
            x.ClientId == clientId && x.Year == year && x.Month == month);
        if (pack is null)
        {
            return NotFound();
        }

        return Ok(pack);
    }

    [HttpPost]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<ActionResult<MonthlyPack>> Create([FromBody] CreateMonthlyPackRequest request)
    {
        var allowedClientIds = await User.GetAccessibleClientIdsAsync(_db);
        if (!allowedClientIds.Contains(request.ClientId) && !User.IsAdmin())
        {
            return Forbid();
        }

        var existing = await _db.MonthlyPacks.FirstOrDefaultAsync(x =>
            x.ClientId == request.ClientId && x.Year == request.Year && x.Month == request.Month);
        if (existing is not null)
        {
            return Ok(existing);
        }

        var pack = new MonthlyPack
        {
            Id = $"mp_{Guid.NewGuid():N}",
            ClientId = request.ClientId,
            Year = request.Year,
            Month = request.Month,
            Status = NormalizeStatus(request.Status),
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        _db.MonthlyPacks.Add(pack);
        await _db.SaveChangesAsync();
        await _db.WriteAuditLogAsync(
            User,
            "monthly_packs.created",
            "monthly_pack",
            pack.Id,
            pack.ClientId,
            JsonSerializer.Serialize(new { pack.ClientId, pack.Year, pack.Month, pack.Status }));
        return Created($"/api/monthly-packs/{pack.ClientId}/{pack.Year}/{pack.Month}", pack);
    }

    [HttpPost("{id}/submit")]
    public async Task<ActionResult<MonthlyPack>> Submit(string id, CancellationToken ct)
    {
        var pack = await _db.MonthlyPacks.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (pack is null)
        {
            return NotFound();
        }

        var allowedClientIds = await User.GetAccessibleClientIdsAsync(_db, ct);
        if (!allowedClientIds.Contains(pack.ClientId))
        {
            return Forbid();
        }

        var requiredSlots = await _db.DocumentSlots
            .Where(x => x.MonthlyPackId == pack.Id && x.IsRequired)
            .ToListAsync(ct);
        if (requiredSlots.Any(x => x.Status == "missing"))
        {
            return BadRequest(new { error = "All required document slots must be uploaded before submission." });
        }

        pack.Status = "submitted";
        pack.SubmittedAtUtc = DateTime.UtcNow;
        pack.UpdatedAtUtc = pack.SubmittedAtUtc.Value;
        await _db.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(
            User,
            "monthly_packs.submitted",
            "monthly_pack",
            pack.Id,
            pack.ClientId,
            JsonSerializer.Serialize(new { pack.ClientId, pack.Year, pack.Month, pack.Status }),
            ct);

        return Ok(pack);
    }

    private static string NormalizeStatus(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "draft" : value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "draft" => "draft",
            "in_progress" => "in_progress",
            "submitted" => "submitted",
            "under_review" => "under_review",
            "completed" => "completed",
            "reopened" => "reopened",
            _ => "draft"
        };
    }
}
