using SecureClientPortal.Backend.Application.Contracts.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Domain.Modules.MonthlyPacks;
using System.Security.Claims;

namespace SecureClientPortal.Backend.Application.Modules.MonthlyPacks;

public interface IDocumentSlotService
{
    Task<(bool forbidden, IReadOnlyList<DocumentSlot>? items)> GetByMonthlyPackIdAsync(string monthlyPackId, ClaimsPrincipal user, CancellationToken ct = default);
    Task<(bool forbidden, DocumentSlot created)> CreateAsync(CreateDocumentSlotRequest request, ClaimsPrincipal user, CancellationToken ct = default);
    Task<(bool forbidden, bool invalid, string? error, DocumentSlot? slot)> SubmitAsync(string slotId, ClaimsPrincipal user, CancellationToken ct = default);
    Task<(bool forbidden, bool invalid, string? error, DocumentSlot? slot)> MarkNotApplicableAsync(string slotId, ClaimsPrincipal user, CancellationToken ct = default);
}
