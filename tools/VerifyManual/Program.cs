using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using SecureClientPortal.Backend.Application.Modules.Banking;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Domain.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Infrastructure.Modules.Banking;
using SecureClientPortal.Backend.Models;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;

using var settings = JsonDocument.Parse(await File.ReadAllTextAsync(args[0]));
var configured = settings.RootElement.GetProperty("ConnectionStrings").GetProperty("DefaultConnection").GetString();
var databaseName = "portal_manual_verify_" + Guid.NewGuid().ToString("N");
var masterBuilder = new SqlConnectionStringBuilder(configured) { InitialCatalog = "master", Pooling = false };
var testBuilder = new SqlConnectionStringBuilder(configured) { InitialCatalog = databaseName, Pooling = false };
await using var master = new SqlConnection(masterBuilder.ConnectionString);
await master.OpenAsync();
await using var masterCommand = master.CreateCommand();
masterCommand.CommandText = $"CREATE DATABASE [{databaseName}]";
await masterCommand.ExecuteNonQueryAsync();
try
{
    var clientId = Guid.NewGuid();
    var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()), new Claim(ClaimTypes.Role, "admin"), new Claim("role_scope", "admin")], "test"));
    PortalDbContext Portal(bool fail = false) {
        var options = new DbContextOptionsBuilder<PortalDbContext>().UseSqlServer(testBuilder.ConnectionString);
        if (fail) options.AddInterceptors(new FailAudit());
        return new PortalDbContext(options.Options);
    }
    BankingDbContext Banks() => new(new DbContextOptionsBuilder<BankingDbContext>().UseSqlServer(testBuilder.ConnectionString).Options);
    BankingService Service(PortalDbContext p, BankingDbContext b) => new(p, b, [], Options.Create(new BankingOptions()));
    void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); Console.WriteLine("PASS " + message); }
    await using (var p = Portal()) {
        await p.Database.EnsureCreatedAsync();
        p.Clients.Add(Client.Create(clientId, "Isolated SQL verification", "Company", "Testing", "test@example.invalid", ClientStatus.Active));
        p.MonthlyPacks.Add(MonthlyPack.Create(Guid.NewGuid(), clientId, 2026, 8));
        await p.SaveChangesAsync();
    }
    await using (var b = Banks())
        foreach (var batch in Regex.Split(b.Database.GenerateCreateScript(), @"(?im)^\s*GO\s*(?:--.*)?$").Where(s => !string.IsNullOrWhiteSpace(s)))
            await b.Database.ExecuteSqlRawAsync(batch);
    async Task<BankingOperationResult<BankingOverviewDto>> Register(ManualBankAccountRequest request, bool fail = false) {
        await using var p = Portal(fail); await using var b = Banks();
        return await Service(p, b).AddManualAccountAsync(request, user);
    }
    var request = new ManualBankAccountRequest(clientId, "Same bank", "Operating", "1234", Guid.NewGuid());
    var first = await Register(request);
    Check(first.Success && first.Value!.Accounts.Count == 1, "First manual account saves with its audit entry");
    await using (var p = Portal()) Check(await p.AuditLogs.AnyAsync(a => a.Action == "banking.manual_account_saved" && a.EntityId == first.Value!.Accounts[0].Id), "Account audit references the saved account identity");
    var repeated = await Register(request);
    Check(repeated.Success && repeated.Value!.Accounts.Count == 1, "Registration retry is idempotent");
    var second = await Register(request with { AccountName = "Savings", RegistrationId = Guid.NewGuid() });
    Check(second.Success && second.Value!.Accounts.Count == 2, "Distinct accounts may share a bank and last four digits");
    Check(!(await Register(request with { AccountName = "Changed payload" })).Success, "Registration identity cannot be reused for different details");
    try { await Register(request with { RegistrationId = Guid.NewGuid(), AccountName = "Must roll back" }, true); throw new Exception("Expected audit failure"); }
    catch (InvalidOperationException e) when (e.Message == "Injected audit failure") { }
    await using (var b = Banks()) Check(await b.BankAccounts.CountAsync() == 2, "Account and statement creation roll back when auditing fails");
    var import = new ManualTransactionImportRequest(first.Value!.Accounts[0].Id, "verification.csv", [new("2026-08-05", "Test debit", "test", -100m, 100m)], false);
    try {
        await using var p = Portal(true); await using var b = Banks();
        await Service(p, b).ImportManualTransactionsAsync(import, user);
        throw new Exception("Expected audit failure");
    } catch (InvalidOperationException e) when (e.Message == "Injected audit failure") { }
    await using (var b = Banks()) Check(await b.BankTransactions.CountAsync() == 0 && await b.BankSyncRuns.CountAsync() == 0, "Transaction rows and import run roll back on audit failure");
    await using (var p = Portal()) { await using var b = Banks(); var result = await Service(p,b).ImportManualTransactionsAsync(import,user); Check(result.Success && result.Value!.Imported == 1, "Successful import commits with audit"); }
    await using (var p = Portal()) { await using var b = Banks(); var result = await Service(p,b).ImportManualTransactionsAsync(import,user); Check(result.Success && result.Value!.Imported == 0 && result.Value.Duplicates == 1, "Repeated SQL import is deduplicated"); }
}
finally
{
    if (!Regex.IsMatch(databaseName, "^portal_manual_verify_[0-9a-f]{32}$")) throw new InvalidOperationException("Unexpected test database name.");
    masterCommand.CommandText = $"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}]";
    await masterCommand.ExecuteNonQueryAsync();
    Console.WriteLine("Removed isolated verification database.");
}

sealed class FailAudit : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
    {
        if (data.Context!.ChangeTracker.Entries().Any(e => e.State == EntityState.Added && e.Entity.GetType().Name == "AuditLog")) throw new InvalidOperationException("Injected audit failure");
        return ValueTask.FromResult(result);
    }
}
