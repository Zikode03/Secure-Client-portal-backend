using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using SecureClientPortal.Backend.Data;

namespace SecureClientPortal.Backend.Infrastructure.EntityFrameworkCore;

public sealed class DesignTimePortalDbContextFactory : IDesignTimeDbContextFactory<PortalDbContext>
{
    public PortalDbContext CreateDbContext(string[] args)
    {
        var connectionString = ResolveConnectionString();
        var optionsBuilder = new DbContextOptionsBuilder<PortalDbContext>();
        optionsBuilder.UseSqlServer(connectionString);
        return new PortalDbContext(optionsBuilder.Options);
    }

    private static string ResolveConnectionString()
    {
        var basePath = Directory.GetCurrentDirectory();
        var apiProjectPath = Path.GetFullPath(Path.Combine(basePath, "..", "SecureClientPortal.Api"));

        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.Exists(apiProjectPath) ? apiProjectPath : basePath)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        return configuration.GetConnectionString("DefaultConnection")
            ?? configuration["DB_CONNECTION_STRING"]
            ?? "Server=localhost,1433;Database=secure_client_portal_dev;User Id=sa;Password=StrongPass!12345;Encrypt=True;TrustServerCertificate=True;MultipleActiveResultSets=True";
    }
}
