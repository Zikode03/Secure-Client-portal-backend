using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureClientPortal.Backend.Application.Contracts;
using SecureClientPortal.Backend.Application.Documents;
using SecureClientPortal.Backend.Models;

namespace SecureClientPortal.Backend.Controllers;

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
    public async Task<ActionResult<IEnumerable<DocumentSlot>>> GetByMonthlyPackId(string monthlyPackId, CancellationToken ct)
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

        return Ok(result.items);
    }

    [HttpPost]
    [Authorize(Policy = "AccountantOnly")]
    public async Task<ActionResult<DocumentSlot>> Create([FromBody] CreateDocumentSlotRequest request, CancellationToken ct)
    {
        try
        {
            var result = await _documentSlotService.CreateAsync(request, User, ct);
            if (result.forbidden)
            {
                return Forbid();
            }

            return Created($"/api/document-slots/{result.created.MonthlyPackId}", result.created);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
