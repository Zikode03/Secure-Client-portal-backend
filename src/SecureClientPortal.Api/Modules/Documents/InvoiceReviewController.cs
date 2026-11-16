using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts.Modules.Documents;
using SecureClientPortal.Backend.Infrastructure.Modules.Documents.Application;

namespace SecureClientPortal.Backend.Api.Modules.Documents;

[ApiController]
[Route("api/documents/{documentId:guid}/invoice-review")]
[Authorize(Policy = "AccountantOnly")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class InvoiceReviewController(InvoiceReviewService service) : ControllerBase
{
    [HttpGet]
    public Task<ActionResult<InvoiceReviewResponse>> Get(Guid documentId, CancellationToken ct) =>
        ExecuteAsync(() => service.GetAsync(documentId, User, ct));

    [HttpPost("corrections")]
    [RequestSizeLimit(100_000)]
    [SecureClientPortal.Backend.Api.Security.RequirePermission("documents.review")]
    public Task<ActionResult<InvoiceReviewResponse>> Save(Guid documentId, SaveInvoiceReviewRequest request, CancellationToken ct) =>
        ExecuteAsync(() => service.SaveAsync(documentId, request, User, ct));

    private async Task<ActionResult<InvoiceReviewResponse>> ExecuteAsync(Func<Task<InvoiceReviewResponse>> action)
    {
        try { return Ok(await action()); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (DbUpdateConcurrencyException) { return Conflict(new { error = "This invoice changed in another session. Refresh the review before trying again." }); }
        catch (DbUpdateException e) when (e.InnerException is Microsoft.Data.SqlClient.SqlException sql && sql.Number is 2601 or 2627 or 1205)
        { return Conflict(new { error = "Another review was saved at the same time. Refresh before trying again." }); }
        catch (Microsoft.Data.SqlClient.SqlException e) when (e.Number == 1205)
        { return Conflict(new { error = "Another review was saved at the same time. Refresh before trying again." }); }
        catch (AppValidationException e) { return BadRequest(new { error = e.Message }); }
    }
}
