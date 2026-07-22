using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Domain.Modules.Documents;
using SecureClientPortal.Backend.Domain.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Models;

namespace SecureClientPortal.Backend.Application.Modules.Documents;

public interface IDocumentModuleDbContext
{
    DbSet<Document> Documents { get; }
    DbSet<DocumentComment> DocumentComments { get; }
    DbSet<FilingRule> FilingRules { get; }
    DbSet<MonthlyPack> MonthlyPacks { get; }
    DbSet<DocumentSlot> DocumentSlots { get; }
    DbSet<DocumentVersion> DocumentVersions { get; }
    DbSet<ReviewDecision> ReviewDecisions { get; }
    DbSet<RequestItem> Requests { get; }
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
