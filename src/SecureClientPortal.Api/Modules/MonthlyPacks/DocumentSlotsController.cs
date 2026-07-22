using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureClientPortal.Backend.Application.Contracts.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Application.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Domain.Modules.MonthlyPacks;

namespace SecureClientPortal.Backend.Api.Modules.MonthlyPacks;

[ApiController]
[Route("api/document-slots")]
[Authorize(Policy = "ClientOrAccountant")]
public class DocumentSlotsController : ControllerBase
{
    private readonly IDocumentSlotService _documentSlotService;

    public DocumentSlotsController(IDocumentSlotService documentSlotService)
    {
        _documentSlotService = documentSlotService;
    }

    [HttpGet("{monthlyPackId}")]
    public async Task<ActionResult<IEnumerable<DocumentSlotResponse>>> GetByMonthlyPackId(string monthlyPackId, CancellationToken ct)
    {
        var result = await _documentSlotService.GetByMonthlyPackIdAsync(monthlyPackId, User, ct);
        if (result.forbidden)
        {
            return Forbid();
        }

        if (result.items is null)
        {
            return NotFound();
        }

        return Ok(result.items.Select(Map));
    }

    [HttpPost]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<ActionResult<DocumentSlotResponse>> Create([FromBody] CreateDocumentSlotRequest request, CancellationToken ct)
    {
        try
        {
            var result = await _documentSlotService.CreateAsync(request, User, ct);
            if (result.forbidden)
            {
                return Forbid();
            }

            return Created($"/api/document-slots/{result.created.MonthlyPackId}", Map(result.created));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("{slotId}/submit")]
    public async Task<ActionResult<DocumentSlotResponse>> Submit(string slotId, CancellationToken ct)
    {
        var result = await _documentSlotService.SubmitAsync(slotId, User, ct);
        if (result.forbidden)
        {
            return Forbid();
        }

        if (result.invalid)
        {
            return BadRequest(new { error = result.error });
        }

        if (result.slot is null)
        {
            return NotFound();
        }

        return Ok(Map(result.slot));
    }

    [HttpPost("{slotId}/not-applicable")]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<ActionResult<DocumentSlotResponse>> MarkNotApplicable(string slotId, CancellationToken ct)
    {
        var result = await _documentSlotService.MarkNotApplicableAsync(slotId, User, ct);
        if (result.forbidden)
        {
            return Forbid();
        }

        if (result.invalid)
        {
            return BadRequest(new { error = result.error });
        }

        if (result.slot is null)
        {
            return NotFound();
        }

        return Ok(Map(result.slot));
    }

    private static DocumentSlotResponse Map(DocumentSlot slot) =>
        new(
            slot.Id,
            slot.MonthlyPackId,
            slot.ClientId,
            slot.Category,
            slot.Label,
            slot.IsRequired,
            slot.Status,
            slot.CanCurrentlyBeSubmitted,
            slot.CurrentDocumentId,
            slot.DueDateUtc,
            slot.SubmittedAtUtc,
            slot.SubmittedByUserId,
            slot.ReviewStatus,
            slot.RejectionReason,
            slot.CreatedAtUtc,
            slot.UpdatedAtUtc);
}
