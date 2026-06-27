using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Models;

namespace SecureClientPortal.Backend.Application.Requests;

public interface IRequestModuleDbContext
{
    DbSet<RequestItem> Requests { get; }
    DbSet<RequestComment> RequestComments { get; }
    DbSet<Document> Documents { get; }
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
