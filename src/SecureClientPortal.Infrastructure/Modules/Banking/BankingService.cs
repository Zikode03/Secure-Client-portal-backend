using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SecureClientPortal.Backend.Application.Modules.Banking;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Domain.Modules.Banking;
using System.Security.Claims;

namespace SecureClientPortal.Backend.Infrastructure.Modules.Banking;

public sealed class BankingService(
    PortalDbContext portalDb,
    BankingDbContext bankingDb,
    IBankDataProvider provider,
    IOptions<BankingOptions> options) : IBankingService
{
    private readonly BankingOptions config = options.Value;

    public async Task<BankingOperationResult<BankingOverviewDto>> GetOverviewAsync(Guid? clientId, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var resolved = await ResolveClientIdAsync(clientId, user, ct);
        if (resolved.forbidden) return BankingOperationResult<BankingOverviewDto>.Denied();
        if (resolved.clientId is null) return BankingOperationResult<BankingOverviewDto>.Fail("A client could not be resolved for the current user.");
        return BankingOperationResult<BankingOverviewDto>.Ok(await BuildOverviewAsync(resolved.clientId.Value, ct));
    }

    public async Task<BankingOperationResult<BankingOverviewDto>> ConnectSandboxAsync(Guid? clientId, ClaimsPrincipal user, CancellationToken ct = default)
    {
        if (!config.SandboxEnabled)
            return BankingOperationResult<BankingOverviewDto>.Fail("Sandbox bank connections are disabled in this environment.");

        var resolved = await ResolveClientIdAsync(clientId, user, ct);
        if (resolved.forbidden) return BankingOperationResult<BankingOverviewDto>.Denied();
        if (resolved.clientId is null) return BankingOperationResult<BankingOverviewDto>.Fail("A client could not be resolved for the current user.");

        var id = resolved.clientId.Value;
        var existing = await bankingDb.BankConnections.AnyAsync(x => x.ClientId == id && x.Status != "disconnected", ct);
        if (existing)
            return BankingOperationResult<BankingOverviewDto>.Fail("This client already has an active bank connection.");

        var providerResult = await provider.ConnectAsync(id, ct);
        var now = DateTime.UtcNow;
        var connection = BankConnection.Create(id, provider.Name, providerResult.ExternalConnectionId, now, providerResult.ConsentExpiresAtUtc);
        bankingDb.BankConnections.Add(connection);
        bankingDb.BankConsentRecords.Add(BankConsentRecord.Create(
            connection.Id,
            id,
            provider.Name,
            providerResult.ConsentScope,
            now,
            providerResult.ConsentExpiresAtUtc));

        await ApplyProviderDataAsync(connection, providerResult.Accounts, providerResult.Transactions, now, ct);
        connection.MarkSynced(now);
        await bankingDb.SaveChangesAsync(ct);
        return BankingOperationResult<BankingOverviewDto>.Ok(await BuildOverviewAsync(id, ct));
    }

    public async Task<BankingOperationResult<BankingOverviewDto>> SyncAsync(Guid connectionId, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var connection = await bankingDb.BankConnections.FirstOrDefaultAsync(x => x.Id == connectionId, ct);
        if (connection is null) return BankingOperationResult<BankingOverviewDto>.Fail("Bank connection was not found.");
        if (!await CanAccessClientAsync(connection.ClientId, user, ct)) return BankingOperationResult<BankingOverviewDto>.Denied();
        if (connection.Status == "disconnected") return BankingOperationResult<BankingOverviewDto>.Fail("Disconnected bank connections cannot be synced.");

        var run = BankSyncRun.Start(connection.Id, connection.ClientId, connection.Provider, DateTime.UtcNow);
        bankingDb.BankSyncRuns.Add(run);
        try
        {
            var result = await provider.SyncAsync(connection.ExternalConnectionId, ct);
            var now = DateTime.UtcNow;
            await ApplyProviderDataAsync(connection, result.Accounts, result.Transactions, now, ct);
            connection.MarkSynced(now);
            run.Complete(result.Transactions.Count, result.FromDateUtc, result.ToDateUtc, now);
            await bankingDb.SaveChangesAsync(ct);
            return BankingOperationResult<BankingOverviewDto>.Ok(await BuildOverviewAsync(connection.ClientId, ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var message = "Bank sync failed. The previous bank data remains unchanged.";
            connection.MarkNeedsAttention(message);
            run.Fail(message, DateTime.UtcNow);
            await bankingDb.SaveChangesAsync(ct);
            return BankingOperationResult<BankingOverviewDto>.Fail(message);
        }
    }

    public async Task<BankingOperationResult<BankingOverviewDto>> DisconnectAsync(Guid connectionId, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var connection = await bankingDb.BankConnections.FirstOrDefaultAsync(x => x.Id == connectionId, ct);
        if (connection is null) return BankingOperationResult<BankingOverviewDto>.Fail("Bank connection was not found.");
        if (!await CanAccessClientAsync(connection.ClientId, user, ct)) return BankingOperationResult<BankingOverviewDto>.Denied();

        await provider.DisconnectAsync(connection.ExternalConnectionId, ct);
        var now = DateTime.UtcNow;
        connection.Disconnect(now);
        var consents = await bankingDb.BankConsentRecords
            .Where(x => x.BankConnectionId == connection.Id && x.RevokedAtUtc == null)
            .ToListAsync(ct);
        foreach (var consent in consents) consent.Revoke(now);
        await bankingDb.SaveChangesAsync(ct);
        return BankingOperationResult<BankingOverviewDto>.Ok(await BuildOverviewAsync(connection.ClientId, ct));
    }

    private async Task ApplyProviderDataAsync(
        BankConnection connection,
        IReadOnlyList<ProviderAccount> accounts,
        IReadOnlyList<ProviderTransaction> transactions,
        DateTime now,
        CancellationToken ct)
    {
        var accountMap = new Dictionary<string, BankAccount>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in accounts)
        {
            var account = await bankingDb.BankAccounts.FirstOrDefaultAsync(
                x => x.BankConnectionId == connection.Id && x.ExternalAccountId == source.ExternalAccountId,
                ct);
            if (account is null)
            {
                account = BankAccount.Create(
                    connection.Id,
                    connection.ClientId,
                    source.ExternalAccountId,
                    source.BankName,
                    source.AccountName,
                    source.AccountType,
                    source.AccountNumberMasked,
                    source.Currency,
                    source.CurrentBalance,
                    source.AvailableBalance,
                    now);
                bankingDb.BankAccounts.Add(account);
            }
            else
            {
                account.RefreshBalances(source.CurrentBalance, source.AvailableBalance, now);
            }
            accountMap[source.ExternalAccountId] = account;
        }

        foreach (var source in transactions)
        {
            if (!accountMap.TryGetValue(source.ExternalAccountId, out var account)) continue;
            var exists = await bankingDb.BankTransactions.AnyAsync(
                x => x.BankAccountId == account.Id && x.ExternalTransactionId == source.ExternalTransactionId,
                ct);
            if (exists) continue;

            bankingDb.BankTransactions.Add(BankTransaction.Create(
                account.Id,
                connection.ClientId,
                source.ExternalTransactionId,
                source.TransactionDateUtc,
                source.PostedDateUtc,
                source.Description,
                source.Reference,
                source.Amount,
                source.Direction,
                source.Balance,
                source.ProviderCategory,
                now));
        }
    }

    private async Task<BankingOverviewDto> BuildOverviewAsync(Guid clientId, CancellationToken ct)
    {
        var connections = await bankingDb.BankConnections
            .Where(x => x.ClientId == clientId)
            .OrderByDescending(x => x.ConnectedAtUtc)
            .ToListAsync(ct);
        var accounts = await bankingDb.BankAccounts
            .Where(x => x.ClientId == clientId)
            .OrderBy(x => x.BankName).ThenBy(x => x.AccountName)
            .ToListAsync(ct);
        var transactions = await bankingDb.BankTransactions
            .Where(x => x.ClientId == clientId)
            .OrderByDescending(x => x.TransactionDateUtc)
            .Take(50)
            .ToListAsync(ct);
        var syncRuns = await bankingDb.BankSyncRuns
            .Where(x => x.ClientId == clientId)
            .OrderByDescending(x => x.StartedAtUtc)
            .Take(20)
            .ToListAsync(ct);

        return new BankingOverviewDto(
            clientId,
            config.SandboxEnabled,
            connections.Select(x => new BankConnectionDto(x.Id, x.ClientId, x.Provider, x.Status, x.ConnectedAtUtc, x.LastSyncedAtUtc, x.ConsentExpiresAtUtc, x.DisconnectedAtUtc, x.FailureReason)).ToList(),
            accounts.Select(x => new BankAccountDto(x.Id, x.BankConnectionId, x.BankName, x.AccountName, x.AccountType, x.AccountNumberMasked, x.Currency, x.CurrentBalance, x.AvailableBalance, x.LastUpdatedAtUtc)).ToList(),
            transactions.Select(x => new BankTransactionDto(x.Id, x.BankAccountId, x.TransactionDateUtc, x.PostedDateUtc, x.Description, x.Reference, x.Amount, x.Direction, x.Balance, x.ProviderCategory)).ToList(),
            syncRuns.Select(x => new BankSyncRunDto(x.Id, x.BankConnectionId, x.Provider, x.StartedAtUtc, x.FinishedAtUtc, x.Status, x.TransactionsReceived, x.FromDateUtc, x.ToDateUtc, x.ErrorMessage)).ToList());
    }

    private async Task<(Guid? clientId, bool forbidden)> ResolveClientIdAsync(Guid? requestedClientId, ClaimsPrincipal user, CancellationToken ct)
    {
        var accessible = await user.GetAccessibleClientIdsAsync(portalDb, ct);
        if (requestedClientId.HasValue)
            return accessible.Contains(requestedClientId.Value) ? (requestedClientId.Value, false) : (null, true);

        if (user.IsClient() && accessible.Count == 1)
            return (accessible.Single(), false);

        return (null, false);
    }

    private async Task<bool> CanAccessClientAsync(Guid clientId, ClaimsPrincipal user, CancellationToken ct)
    {
        var accessible = await user.GetAccessibleClientIdsAsync(portalDb, ct);
        return accessible.Contains(clientId);
    }
}
