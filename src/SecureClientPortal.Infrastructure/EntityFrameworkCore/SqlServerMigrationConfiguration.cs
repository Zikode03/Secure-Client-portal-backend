using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using SecureClientPortal.Backend.Data;

namespace SecureClientPortal.Backend.Infrastructure.EntityFrameworkCore;

public static class SqlServerMigrationConfiguration
{
    public const string PortalHistoryTable = "__EFMigrationsHistory";
    public const string BankingHistoryTable = "__EFMigrationsHistory_Banking";
    public const string Schema = "dbo";
    public const string BankingFoundationId = "20260916180000_BankingFoundation";

    public static void Portal(SqlServerDbContextOptionsBuilder options) => options
        .MigrationsAssembly(typeof(PortalDbContext).Assembly.GetName().Name!)
        .MigrationsHistoryTable(PortalHistoryTable, Schema);

    public static void Banking(SqlServerDbContextOptionsBuilder options) => options
        .MigrationsAssembly(typeof(BankingDbContext).Assembly.GetName().Name!)
        .MigrationsHistoryTable(BankingHistoryTable, Schema);

    public static async Task AssertBankingHistorySafeAsync(BankingDbContext db, CancellationToken ct = default)
    {
        if (!(await db.Database.GetPendingMigrationsAsync(ct)).Contains(BankingFoundationId)) return;
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID(N'dbo') " +
                "AND name IN (N'AppBankConnections',N'AppBankAccounts',N'AppBankTransactions',N'AppBankSyncRuns',N'AppBankConsentRecords')";
            if (Convert.ToInt32(await command.ExecuteScalarAsync(ct)) > 0)
                throw new InvalidOperationException("Banking tables already exist without their dedicated migration history. " +
                    "Verify the schema and explicitly adopt the legacy Banking history before starting the API. No Banking migration was applied.");
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }
}
