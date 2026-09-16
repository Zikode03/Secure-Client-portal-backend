using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SecureClientPortal.Backend.Data;

namespace SecureClientPortal.Backend.Infrastructure.EntityFrameworkCore;

public sealed class DesignTimeBankingDbContextFactory : IDesignTimeDbContextFactory<BankingDbContext>
{
    public BankingDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<BankingDbContext>();
        options.UseSqlServer(DesignTimeDatabaseConfiguration.ConnectionString(args), SqlServerMigrationConfiguration.Banking);
        return new BankingDbContext(options.Options);
    }
}
