using System.Security.Claims;

namespace SecureClientPortal.Backend.Application.Modules.Banking;

public sealed record BankConnectionDto(
    Guid Id,
    Guid ClientId,
    string Provider,
    string Status,
    DateTime ConnectedAtUtc,
    DateTime? LastSyncedAtUtc,
    DateTime? ConsentExpiresAtUtc,
    DateTime? DisconnectedAtUtc,
    string? FailureReason);

public sealed record BankAccountDto(
    Guid Id,
    Guid BankConnectionId,
    string BankName,
    string AccountName,
    string AccountType,
    string AccountNumberMasked,
    string Currency,
    decimal? CurrentBalance,
    decimal? AvailableBalance,
    DateTime LastUpdatedAtUtc);

public sealed record BankTransactionDto(
    Guid Id,
    Guid BankAccountId,
    DateTime TransactionDateUtc,
    DateTime? PostedDateUtc,
    string Description,
    string Reference,
    decimal Amount,
    string Direction,
    decimal? Balance,
    string ProviderCategory);

public sealed record BankSyncRunDto(
    Guid Id,
    Guid BankConnectionId,
    string Provider,
    DateTime StartedAtUtc,
    DateTime? FinishedAtUtc,
    string Status,
    int TransactionsReceived,
    DateTime? FromDateUtc,
    DateTime? ToDateUtc,
    string? ErrorMessage);

public sealed record BankingOverviewDto(
    Guid ClientId,
    bool SandboxEnabled,
    IReadOnlyList<BankConnectionDto> Connections,
    IReadOnlyList<BankAccountDto> Accounts,
    IReadOnlyList<BankTransactionDto> RecentTransactions,
    IReadOnlyList<BankSyncRunDto> SyncRuns);

public sealed record MonthlyPackBankingStatusDto(
    Guid ClientId,
    int Year,
    int Month,
    string Status,
    bool HasActiveConnection,
    bool IsPeriodComplete,
    int ConnectedAccountCount,
    DateTime PeriodStartUtc,
    DateTime PeriodEndUtc,
    DateTime RequiredThroughUtc,
    DateTime? DataFromUtc,
    DateTime? DataThroughUtc,
    DateTime? MissingFromUtc,
    DateTime? MissingToUtc,
    string Message);

public sealed record BankingOperationResult<T>(bool Success, bool Forbidden, string? Error, T? Value)
{
    public static BankingOperationResult<T> Ok(T value) => new(true, false, null, value);
    public static BankingOperationResult<T> Denied() => new(false, true, null, default);
    public static BankingOperationResult<T> Fail(string error) => new(false, false, error, default);
}

public sealed record ProviderAccount(
    string ExternalAccountId,
    string BankName,
    string AccountName,
    string AccountType,
    string AccountNumberMasked,
    string Currency,
    decimal? CurrentBalance,
    decimal? AvailableBalance);

public sealed record ProviderTransaction(
    string ExternalAccountId,
    string ExternalTransactionId,
    DateTime TransactionDateUtc,
    DateTime? PostedDateUtc,
    string Description,
    string Reference,
    decimal Amount,
    string Direction,
    decimal? Balance,
    string ProviderCategory);

public sealed record ProviderConnectionResult(
    string ExternalConnectionId,
    DateTime? ConsentExpiresAtUtc,
    string ConsentScope,
    IReadOnlyList<ProviderAccount> Accounts,
    IReadOnlyList<ProviderTransaction> Transactions,
    DateTime? FromDateUtc,
    DateTime? ToDateUtc);

public sealed record ProviderSyncResult(
    IReadOnlyList<ProviderAccount> Accounts,
    IReadOnlyList<ProviderTransaction> Transactions,
    DateTime? FromDateUtc,
    DateTime? ToDateUtc);

public interface IBankDataProvider
{
    string Name { get; }
    Task<ProviderConnectionResult> ConnectAsync(Guid clientId, CancellationToken ct = default);
    Task<ProviderSyncResult> SyncAsync(string externalConnectionId, CancellationToken ct = default);
    Task DisconnectAsync(string externalConnectionId, CancellationToken ct = default);
}

public interface IBankingService
{
    Task<BankingOperationResult<BankingOverviewDto>> GetOverviewAsync(Guid? clientId, ClaimsPrincipal user, CancellationToken ct = default);
    Task<BankingOperationResult<MonthlyPackBankingStatusDto>> GetMonthlyPackStatusAsync(Guid clientId, int year, int month, ClaimsPrincipal user, CancellationToken ct = default);
    Task<BankingOperationResult<BankingOverviewDto>> ConnectSandboxAsync(Guid? clientId, ClaimsPrincipal user, CancellationToken ct = default);
    Task<BankingOperationResult<BankingOverviewDto>> SyncAsync(Guid connectionId, ClaimsPrincipal user, CancellationToken ct = default);
    Task<BankingOperationResult<BankingOverviewDto>> DisconnectAsync(Guid connectionId, ClaimsPrincipal user, CancellationToken ct = default);
}
