using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Infrastructure.EntityFrameworkCore;
using System.Text.Json;
using BankingFoundation = SecureClientPortal.Infrastructure.EntityFrameworkCore.MigrationsSqlServer.Banking.BankingFoundation;

namespace SecureClientPortal.Backend.Tests;

public class DatabaseMigrationTests
{
    [Fact]
    public void BankingModel_MatchesAlreadyAppliedFoundation_WithoutOwningPortalTables()
    {
        using var db = new BankingDbContext(new DbContextOptionsBuilder<BankingDbContext>()
            .UseSqlServer("Server=localhost;Database=migration_model_checks;Integrated Security=true;Encrypt=true").Options);
        var model = db.GetService<IDesignTimeModel>().Model;
        var expected = db.GetService<IMigrationsModelDiffer>().GetDifferences(null, model.GetRelationalModel());
        var applied = new BankingFoundation().UpOperations;
        Assert.Equal(Normalise(applied), Normalise(expected));
        Assert.Equal(5, expected.OfType<CreateTableOperation>().Count());
        Assert.DoesNotContain(expected.OfType<CreateTableOperation>(), x => x.Name == "AppClients");
        Assert.Equal(9, expected.OfType<CreateTableOperation>().Sum(x => x.ForeignKeys.Count));
    }

    private static string Normalise(IEnumerable<MigrationOperation> operations) => JsonSerializer.Serialize(new
    {
        Tables = operations.OfType<CreateTableOperation>().OrderBy(x => x.Name).Select(table => new
        {
            table.Name, table.Schema,
            Columns = table.Columns.OrderBy(x => x.Name).Select(x => new
            {
                x.Name, x.ColumnType, x.IsNullable, x.MaxLength, x.Precision, x.Scale,
                x.DefaultValue, x.DefaultValueSql, x.ComputedColumnSql, x.IsRowVersion,
            }),
            PrimaryKey = new { table.PrimaryKey!.Name, table.PrimaryKey.Columns },
            ForeignKeys = table.ForeignKeys.OrderBy(x => x.Name).Select(x => new
            {
                x.Name, x.Columns, x.PrincipalTable, x.PrincipalSchema, x.PrincipalColumns, x.OnDelete, x.OnUpdate,
            }),
            Checks = table.CheckConstraints.OrderBy(x => x.Name).Select(x => new { x.Name, x.Sql }),
        }),
        Indexes = operations.OfType<CreateIndexOperation>().OrderBy(x => x.Name).Select(x => new
        {
            x.Name, x.Table, x.Schema, x.Columns, x.IsUnique, x.Filter,
        }),
        OtherOperations = operations.Where(x => x is not (CreateTableOperation or CreateIndexOperation))
            .Select(x => x.GetType().Name).OrderBy(x => x),
    });

    [Fact]
    public void Contexts_DiscoverDisjointMigrationChains_AndHaveSeparateHistoryTables()
    {
        using var portal = new PortalDbContext(new DbContextOptionsBuilder<PortalDbContext>()
            .UseSqlServer("Server=localhost;Database=migration_model_checks;Integrated Security=true;Encrypt=true", SqlServerMigrationConfiguration.Portal).Options);
        using var banking = new BankingDbContext(new DbContextOptionsBuilder<BankingDbContext>()
            .UseSqlServer("Server=localhost;Database=migration_model_checks;Integrated Security=true;Encrypt=true", SqlServerMigrationConfiguration.Banking).Options);
        var portalMigrations = portal.Database.GetMigrations().ToArray();
        var bankingMigrations = banking.Database.GetMigrations().ToArray();
        Assert.Equal(10, portalMigrations.Length);
        Assert.Equal([SqlServerMigrationConfiguration.BankingFoundationId], bankingMigrations);
        Assert.Empty(portalMigrations.Intersect(bankingMigrations));
        Assert.Contains("[dbo].[__EFMigrationsHistory]", portal.GetService<IHistoryRepository>().GetCreateScript());
        Assert.Contains("[dbo].[__EFMigrationsHistory_Banking]", banking.GetService<IHistoryRepository>().GetCreateScript());
        Assert.Equal(typeof(PortalDbContext).Assembly, portal.GetService<IMigrationsAssembly>().Assembly);
        Assert.Equal(typeof(BankingDbContext).Assembly, banking.GetService<IMigrationsAssembly>().Assembly);
        Assert.False(portal.Database.HasPendingModelChanges());
        Assert.False(banking.Database.HasPendingModelChanges());
        Assert.Equal(5, new BankingFoundation().TargetModel.GetEntityTypes().Count(x => x.GetTableName()!.StartsWith("AppBank")));
    }
}
