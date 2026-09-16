using SecureClientPortal.Backend.Application.Modules.Banking;

namespace SecureClientPortal.Backend.Infrastructure.Modules.Banking;

public sealed class BankingOptions
{
    public const string Section = "Banking";
    public bool SandboxEnabled { get; set; }
}

public sealed class MockBankDataProvider : IBankDataProvider
{
    public string Name => "sandbox";

    public Task<ProviderConnectionResult> ConnectAsync(Guid clientId, CancellationToken ct = default)
    {
        var externalConnectionId = $"sandbox-{Guid.NewGuid():N}";
        var now = DateTime.UtcNow;
        var accounts = BuildAccounts(externalConnectionId, now);
        var transactions = BuildTransactions(externalConnectionId, now);
        return Task.FromResult(new ProviderConnectionResult(
            externalConnectionId,
            now.AddDays(90),
            "accounts balances transactions",
            accounts,
            transactions));
    }

    public Task<ProviderSyncResult> SyncAsync(string externalConnectionId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var accounts = BuildAccounts(externalConnectionId, now);
        var transactions = BuildTransactions(externalConnectionId, now);
        var from = transactions.Count == 0 ? null : transactions.Min(x => x.TransactionDateUtc);
        var to = transactions.Count == 0 ? null : transactions.Max(x => x.TransactionDateUtc);
        return Task.FromResult(new ProviderSyncResult(accounts, transactions, from, to));
    }

    public Task DisconnectAsync(string externalConnectionId, CancellationToken ct = default) => Task.CompletedTask;

    private static IReadOnlyList<ProviderAccount> BuildAccounts(string connectionId, DateTime now) =>
        [
            new ProviderAccount(
                $"acct-{connectionId}",
                "Sandbox Bank",
                "Business Current Account",
                "current",
                "•••• 4521",
                "ZAR",
                128450.75m,
                124900.25m)
        ];

    private static IReadOnlyList<ProviderTransaction> BuildTransactions(string connectionId, DateTime now)
    {
        var accountId = $"acct-{connectionId}";
        var start = now.Date.AddDays(-4);
        var rows = new (string Description, string Reference, decimal Amount, string Direction, string Category)[]
        {
            ("Client payment", "INV-1048", 18500m, "credit", "income"),
            ("Office supplies", "POS 8452", -1249.50m, "debit", "office"),
            ("Software subscription", "SAAS-SEP", -899m, "debit", "software"),
            ("Supplier payment", "SUP-302", -7420m, "debit", "supplier"),
            ("Client payment", "INV-1051", 9600m, "credit", "income")
        };

        decimal runningBalance = 110000m;
        var result = new List<ProviderTransaction>();
        for (var i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
            runningBalance += row.Amount;
            var date = start.AddDays(i).AddHours(9 + i);
            result.Add(new ProviderTransaction(
                accountId,
                $"tx-{connectionId}-{date:yyyyMMdd}-{i}",
                date,
                date,
                row.Description,
                row.Reference,
                Math.Abs(row.Amount),
                row.Direction,
                runningBalance,
                row.Category));
        }
        return result;
    }
}
