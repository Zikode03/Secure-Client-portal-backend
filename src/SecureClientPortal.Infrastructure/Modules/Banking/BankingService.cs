using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SecureClientPortal.Backend.Application.Contracts.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Application.Modules.Banking;
using SecureClientPortal.Backend.Application.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Domain.Modules.Banking;
using System.Security.Claims;

namespace SecureClientPortal.Backend.Infrastructure.Modules.Banking;

public sealed class BankingService(
    PortalDbContext portalDb,
    BankingDbContext bankingDb,
    IBankDataProvider provider,
    IClientMonthlyPackProfileService monthlyPackProfiles,
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

    public async Task<BankingOperationResult<MonthlyPackBankingStatusDto>> GetMonthlyPackStatusAsync(
        Guid clientId,
        int year,
        int month,
        ClaimsPrincipal user,
        CancellationToken ct = default)
    {
        if (year is < 2000 or > 2200 || month is < 1 or > 12)
            return BankingOperationResult<MonthlyPackBankingStatusDto>.Fail("A valid monthly-pack year and month are required.");
        if (!await CanAccessClientAsync(clientId, user, ct))
            return BankingOperationResult<MonthlyPackBankingStatusDto>.Denied();

        var periodStart = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd = periodStart.AddMonths(1).AddDays(-1);
        var today = DateTime.UtcNow.Date;
        var requiredThrough = periodStart.Date > today
            ? periodStart.Date
            : periodEnd.Date < today ? periodEnd.Date : today;

        var connections = await bankingDb.BankConnections
            .Where(x => x.ClientId == clientId && x.Status != "disconnected")
            .OrderBy(x => x.ConnectedAtUtc)
            .ToListAsync(ct);
        if (connections.Count == 0)
        {
            return BankingOperationResult<MonthlyPackBankingStatusDto>.Ok(new MonthlyPackBankingStatusDto(
                clientId, year, month, "not_connected", false, false, 0,
                periodStart, periodEnd, requiredThrough,
                null, null, periodStart, requiredThrough,
                "No business bank account is connected. Bank statements or another approved source are still required for this monthly pack."));
        }

        var connectionIds = connections.Select(x => x.Id).ToHashSet();
        var accountCount = await bankingDb.BankAccounts.CountAsync(
            x => x.ClientId == clientId && connectionIds.Contains(x.BankConnectionId), ct);
        var runs = await bankingDb.BankSyncRuns
            .Where(x => x.ClientId == clientId && connectionIds.Contains(x.BankConnectionId) && x.Status == "completed" && x.FromDateUtc != null && x.ToDateUtc != null)
            .OrderBy(x => x.FromDateUtc)
            .ToListAsync(ct);

        var coverageByConnection = new List<ConnectionCoverage>();
        foreach (var connection in connections)
        {
            var intervals = runs
                .Where(x => x.BankConnectionId == connection.Id && x.FromDateUtc.HasValue && x.ToDateUtc.HasValue)
                .Select(x => new CoverageInterval(x.FromDateUtc!.Value.Date, x.ToDateUtc!.Value.Date))
                .Where(x => x.End >= periodStart.Date && x.Start <= requiredThrough)
                .OrderBy(x => x.Start)
                .ToList();
            coverageByConnection.Add(CalculateCoverage(connection.Id, intervals, periodStart.Date, requiredThrough));
        }

        var firstGap = coverageByConnection
            .Where(x => x.MissingFrom.HasValue)
            .OrderBy(x => x.MissingFrom)
            .FirstOrDefault();
        var allCovered = coverageByConnection.All(x => x.MissingFrom is null);
        var hasAttention = connections.Any(x => x.Status == "needs_attention");
        var fullCalendarPeriodReached = periodEnd.Date <= today;
        var isPeriodComplete = allCovered && fullCalendarPeriodReached;

        var dataFrom = coverageByConnection
            .Where(x => x.DataFrom.HasValue)
            .Select(x => x.DataFrom!.Value)
            .DefaultIfEmpty()
            .Min();
        var hasDataFrom = coverageByConnection.Any(x => x.DataFrom.HasValue);
        var dataThrough = coverageByConnection
            .Where(x => x.DataThrough.HasValue)
            .Select(x => x.DataThrough!.Value)
            .DefaultIfEmpty()
            .Min();
        var hasDataThrough = coverageByConnection.Any(x => x.DataThrough.HasValue);

        string status;
        string message;
        if (hasAttention)
        {
            status = "needs_attention";
            message = "One or more bank connections need attention. Existing imported data remains available, but the connection should be fixed before the monthly pack is finalised.";
        }
        else if (!allCovered)
        {
            status = "incomplete";
            message = firstGap?.MissingFrom is not null
                ? $"Bank data is incomplete. Coverage is missing from {firstGap.MissingFrom:dd MMM yyyy} to {firstGap.MissingTo:dd MMM yyyy}."
                : "Bank data is incomplete for this monthly pack period.";
        }
        else if (isPeriodComplete)
        {
            status = "complete";
            message = "Bank data covers the full monthly-pack period for every connected bank connection.";
        }
        else
        {
            status = "current";
            message = $"Bank data is current through {requiredThrough:dd MMM yyyy}. The month is still in progress, so final monthly completeness will be confirmed at period end.";
        }

        return BankingOperationResult<MonthlyPackBankingStatusDto>.Ok(new MonthlyPackBankingStatusDto(
            clientId,
            year,
            month,
            status,
            true,
            isPeriodComplete,
            accountCount,
            periodStart,
            periodEnd,
            requiredThrough,
            hasDataFrom ? DateTime.SpecifyKind(dataFrom, DateTimeKind.Utc) : null,
            hasDataThrough ? DateTime.SpecifyKind(dataThrough, DateTimeKind.Utc) : null,
            firstGap?.MissingFrom is null ? null : DateTime.SpecifyKind(firstGap.MissingFrom.Value, DateTimeKind.Utc),
            firstGap?.MissingTo is null ? null : DateTime.SpecifyKind(firstGap.MissingTo.Value, DateTimeKind.Utc),
            message));
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
        var initialRun = BankSyncRun.Start(connection.Id, id, provider.Name, now);
        initialRun.Complete(providerResult.Transactions.Count, providerResult.FromDateUtc, providerResult.ToDateUtc, now);
        bankingDb.BankSyncRuns.Add(initialRun);
        connection.MarkSynced(now);
        await bankingDb.SaveChangesAsync(ct);
        await ReconcileMonthlyPackBankFeedAsync(id, true, user, ct);
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

        var stillConnected = await bankingDb.BankConnections.AnyAsync(
            x => x.ClientId == connection.ClientId && x.Status != "disconnected", ct);
        await ReconcileMonthlyPackBankFeedAsync(connection.ClientId, stillConnected, user, ct);
        return BankingOperationResult<BankingOverviewDto>.Ok(await BuildOverviewAsync(connection.ClientId, ct));
    }

    private async Task ReconcileMonthlyPackBankFeedAsync(Guid clientId, bool connected, ClaimsPrincipal user, CancellationToken ct)
    {
        var currentResult = await monthlyPackProfiles.GetAsync(clientId, user, ct);
        if (!currentResult.IsSuccess || currentResult.Value is null) return;

        var current = currentResult.Value;
        var operating = current.OperatingProfile;
        var operatingInput = new ClientOperatingProfileInput(
            VatRegistered: operating?.VatRegistered,
            VatCycleMonths: operating?.VatCycleMonths ?? 2,
            VatAnchorMonth: operating?.VatAnchorMonth ?? 1,
            HasEmployees: operating?.HasEmployees,
            HoldsInventory: operating?.HoldsInventory,
            UsesSupplierAccounts: operating?.UsesSupplierAccounts,
            UsesPos: operating?.UsesPos,
            OperatesFleet: operating?.OperatesFleet,
            UsesSubcontractors: operating?.UsesSubcontractors,
            UsesPaymentCertificates: operating?.UsesPaymentCertificates,
            TracksProjectCosts: operating?.TracksProjectCosts,
            UsesBookingPlatforms: operating?.UsesBookingPlatforms,
            UsesFoodSuppliers: operating?.UsesFoodSuppliers,
            ManufacturesGoods: operating?.ManufacturesGoods,
            BankFeedConnected: connected,
            SalesInvoicesSynced: operating?.SalesInvoicesSynced ?? false,
            PurchaseInvoicesSynced: operating?.PurchaseInvoicesSynced ?? false);
        var recurring = current.RecurringItems
            .Where(x => string.Equals(x.Source, "client_specific", StringComparison.OrdinalIgnoreCase))
            .Select(x => new ClientMonthlyPackProfileItemInput(
                x.Category,
                x.Label,
                x.IsRequired,
                x.DefaultDueDayOfMonth,
                x.Cadence,
                x.EffectiveFromUtc,
                x.EffectiveToUtc))
            .ToArray();

        await monthlyPackProfiles.UpdateAsync(
            clientId,
            new UpdateClientMonthlyPackProfileRequest(
                current.TemplateId,
                recurring,
                operatingInput,
                DateTime.UtcNow,
                ReconcileCurrentPack: true),
            user,
            ct);
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

    private static ConnectionCoverage CalculateCoverage(Guid connectionId, IReadOnlyList<CoverageInterval> intervals, DateTime requiredStart, DateTime requiredEnd)
    {
        if (requiredEnd < requiredStart)
            return new ConnectionCoverage(connectionId, null, null, null, null);
        if (intervals.Count == 0)
            return new ConnectionCoverage(connectionId, null, null, requiredStart, requiredEnd);

        var merged = new List<CoverageInterval>();
        foreach (var interval in intervals)
        {
            var start = interval.Start < requiredStart ? requiredStart : interval.Start;
            var end = interval.End > requiredEnd ? requiredEnd : interval.End;
            if (end < start) continue;

            if (merged.Count == 0 || start > merged[^1].End.AddDays(1))
            {
                merged.Add(new CoverageInterval(start, end));
            }
            else if (end > merged[^1].End)
            {
                merged[^1] = merged[^1] with { End = end };
            }
        }

        if (merged.Count == 0)
            return new ConnectionCoverage(connectionId, null, null, requiredStart, requiredEnd);

        var cursor = requiredStart;
        foreach (var interval in merged)
        {
            if (interval.Start > cursor)
                return new ConnectionCoverage(connectionId, merged[0].Start, cursor.AddDays(-1), cursor, interval.Start.AddDays(-1));
            if (interval.End >= cursor)
                cursor = interval.End.AddDays(1);
            if (cursor > requiredEnd) break;
        }

        if (cursor <= requiredEnd)
            return new ConnectionCoverage(connectionId, merged[0].Start, cursor.AddDays(-1), cursor, requiredEnd);

        return new ConnectionCoverage(connectionId, merged[0].Start, requiredEnd, null, null);
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

    private sealed record CoverageInterval(DateTime Start, DateTime End);
    private sealed record ConnectionCoverage(Guid ConnectionId, DateTime? DataFrom, DateTime? DataThrough, DateTime? MissingFrom, DateTime? MissingTo);
}
