using Microsoft.Extensions.Configuration;

namespace SecureClientPortal.Backend.Infrastructure.EntityFrameworkCore;

internal static class DesignTimeDatabaseConfiguration
{
    public static string ConnectionString(string[] args)
    {
        var current = Directory.GetCurrentDirectory();
        var paths = new[] { current, Path.Combine(current, "..", "SecureClientPortal.Api"), Path.Combine(current, "src", "SecureClientPortal.Api") };
        var apiPath = paths.Select(Path.GetFullPath).FirstOrDefault(path => File.Exists(Path.Combine(path, "SecureClientPortal.Api.csproj")))
            ?? throw new InvalidOperationException("Run EF tooling from the solution, Infrastructure or API project directory.");
        var commandLine = new ConfigurationBuilder().AddCommandLine(args).Build();
        var environment = commandLine["environment"] ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
        var configuration = new ConfigurationBuilder().SetBasePath(apiPath)
            .AddJsonFile("appsettings.json", optional: false).AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddEnvironmentVariables().Build();
        var connection = configuration["DB_CONNECTION_STRING"];
        if (string.IsNullOrWhiteSpace(connection)) connection = configuration.GetConnectionString("DefaultConnection");
        return !string.IsNullOrWhiteSpace(connection) ? connection :
            throw new InvalidOperationException("Set DB_CONNECTION_STRING or ConnectionStrings:DefaultConnection before running EF tooling.");
    }
}
