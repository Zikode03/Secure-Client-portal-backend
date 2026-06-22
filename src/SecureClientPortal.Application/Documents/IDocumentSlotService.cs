using SecureClientPortal.Backend.Application.Contracts;
using SecureClientPortal.Backend.Models;
using System.Security.Claims;

namespace SecureClientPortal.Backend.Application.Documents;

public interface IDocumentSlotService
{
    Task<(bool forbidden, IReadOnlyList<DocumentSlot>? items)> GetByMonthlyPackIdAsync(string monthlyPackId, ClaimsPrincipal user, CancellationToken ct = default);
    Task<(bool forbidden, DocumentSlot created)> CreateAsync(CreateDocumentSlotRequest request, ClaimsPrincipal user, CancellationToken ct = default);
}
