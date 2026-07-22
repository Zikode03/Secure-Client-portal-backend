using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Domain.Modules.Documents;
using SecureClientPortal.Backend.Models;

namespace SecureClientPortal.Backend.Application.Modules.Requests;

public interface IRequestModuleDbContext
{
    DbSet<RequestItem> Requests { get; }
    DbSet<RequestComment> RequestComments { get; }
    DbSet<Document> Documents { get; }
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
