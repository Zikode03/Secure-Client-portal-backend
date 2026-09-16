using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SecureClientPortal.Backend.Data;

namespace SecureClientPortal.Backend.Infrastructure.EntityFrameworkCore;

public sealed class DesignTimePortalDbContextFactory : IDesignTimeDbContextFactory<PortalDbContext>
{
    public PortalDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PortalDbContext>();
        options.UseSqlServer(DesignTimeDatabaseConfiguration.ConnectionString(args), SqlServerMigrationConfiguration.Portal);
        return new PortalDbContext(options.Options);
    }
}
