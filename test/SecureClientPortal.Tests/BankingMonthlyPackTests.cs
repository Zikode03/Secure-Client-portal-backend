using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SecureClientPortal.Backend.Application.Modules.Banking;
using SecureClientPortal.Backend.Application.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Application.Contracts.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Domain.Modules.Banking;
using SecureClientPortal.Backend.Domain.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Infrastructure.DependencyInjection;
using SecureClientPortal.Backend.Infrastructure.Modules.Banking;
using SecureClientPortal.Backend.Infrastructure.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Models;
using System.Security.Claims;

namespace SecureClientPortal.Backend.Tests;

public class BankingMonthlyPackTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static DateTime Day(int day, int month = 8) => new(2026, month, day, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task NoConnection_RemainsRequiredManualSlot()
    {
        await using var f = await Fixture.Create();
        await f.Reconcile();
        Assert.Equal("not_started", f.Slot.Status);
        Assert.True(f.Slot.IsRequired);
        Assert.Equal("not_connected", (await f.Status()).Status);
    }

    [Fact]
    public async Task ConnectedBank_KeepsVisibleSlotSuppliedByBanking_AndRecordsInitialSync()
    {
        await using var f = await Fixture.Create();
        var result = await f.Banking.ConnectSandboxAsync(f.ClientId, f.User, Ct);
        Assert.True(result.Success);
        Assert.Equal("not_applicable", f.Slot.Status);
        Assert.Single(await f.Db.DocumentSlots.ToListAsync(Ct));
        var run = Assert.Single(result.Value!.SyncRuns);
        Assert.Equal("completed", run.Status);
        Assert.Equal(Day(1), run.FromDateUtc);
        Assert.Equal(Day(31), run.ToDateUtc);
        Assert.Equal("complete", (await f.Status()).Status);
    }

    [Fact]
    public async Task DisconnectFinalBank_RestoresManualRequirement_ForAllOpenPacks()
    {
        await using var f = await Fixture.Create();
        var older = MonthlyPack.Create(Guid.NewGuid(), f.ClientId, 2026, 7);
        f.Db.MonthlyPacks.Add(older);
        await f.Db.SaveChangesAsync(Ct);
        var result = await f.Banking.ConnectSandboxAsync(f.ClientId, f.User, Ct);
        Assert.All(await f.Db.DocumentSlots.ToListAsync(Ct), x => Assert.Equal("not_applicable", x.Status));
        await f.Banking.DisconnectAsync(result.Value!.Connections.Single().Id, f.User, Ct);
        Assert.All(await f.Db.DocumentSlots.ToListAsync(Ct), x => { Assert.True(x.IsRequired); Assert.Equal("not_started", x.Status); });
        Assert.Single(await f.Banks.BankAccounts.ToListAsync(Ct));
        Assert.Single(await f.Banks.BankSyncRuns.ToListAsync(Ct));
    }

    [Fact]
    public async Task TwoBanks_BothFullCoverage_Complete()
    {
        await using var f = await Fixture.Create();
        await f.AddBank("test-alpha", (1, 31));
        await f.AddBank("test-beta", (1, 31));
        var status = await f.Status();
        Assert.Equal("complete", status.Status);
        Assert.True(status.IsPeriodComplete);
        Assert.Equal(2, status.ConnectedAccountCount);
    }

    [Fact]
    public async Task TwoBanks_OneIncomplete_BlocksWholePeriod()
    {
        await using var f = await Fixture.Create();
        await f.AddBank("test-alpha", (1, 31));
        await f.AddBank("test-beta", (1, 21));
        var status = await f.Status();
        Assert.Equal("incomplete", status.Status);
        Assert.False(status.IsPeriodComplete);
        Assert.Equal(Day(22), status.MissingFromUtc);
        Assert.Equal(Day(31), status.MissingToUtc);
        Assert.Equal(Day(21), status.DataThroughUtc);
    }

    [Fact]
    public async Task BankWithoutAnyCoverage_DoesNotBorrowOtherBanksDataThrough()
    {
        await using var f = await Fixture.Create();
        await f.AddBank("test-alpha", (1, 31));
        await f.AddBank("test-beta");
        var status = await f.Status();
        Assert.Equal("incomplete", status.Status);
        Assert.Null(status.DataFromUtc);
        Assert.Null(status.DataThroughUtc);
    }

    [Fact]
    public async Task AdjacentAndOverlappingRanges_Merge_WithoutTransactions()
    {
        await using var f = await Fixture.Create();
        await f.AddBank("test-alpha", (1, 10), (11, 20), (18, 25), (26, 31));
        Assert.Empty(await f.Banks.BankTransactions.ToListAsync(Ct));
        var status = await f.Status();
        Assert.Equal("complete", status.Status);
        Assert.Null(status.MissingFromUtc);
    }

    [Fact]
    public async Task GapDetection_ReturnsFirstMissingRange()
    {
        await using var f = await Fixture.Create();
        await f.AddBank("test-alpha", (1, 10), (15, 31));
        var status = await f.Status();
        Assert.Equal(Day(11), status.MissingFromUtc);
        Assert.Equal(Day(14), status.MissingToUtc);
        Assert.Equal(Day(10), status.DataThroughUtc);
    }

    [Fact]
    public async Task InitialGap_DoesNotClaimCoverageBeforePeriod()
    {
        await using var f = await Fixture.Create();
        await f.AddBank("test-alpha", (15, 31));
        var status = await f.Status();
        Assert.Equal(Day(1), status.MissingFromUtc);
        Assert.Equal(Day(14), status.MissingToUtc);
        Assert.Null(status.DataThroughUtc);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(30)]
    public async Task CurrentMonth_ThroughToday_IsCurrent_NotComplete(int today)
    {
        await using var f = await Fixture.Create(Day(today, 9), 9);
        await f.AddBank("test-alpha", (1, today));
        var status = await f.Status();
        Assert.Equal("current", status.Status);
        Assert.False(status.IsPeriodComplete);
        Assert.Equal(Day(today, 9), status.RequiredThroughUtc);
    }

    [Fact]
    public async Task HistoricalFullMonth_IsComplete()
    {
        await using var f = await Fixture.Create(Day(1, 10), 9);
        await f.AddBank("test-alpha", (1, 30));
        Assert.True((await f.Status()).IsPeriodComplete);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IncompleteCoverage_BlocksSubmitAndClose(bool close)
    {
        await using var f = await Fixture.Create();
        await f.AddBank("test-alpha", (1, 21));
        var result = close ? await f.Workflow.CloseAsync(f.Pack.Id.ToString(), f.User, Ct)
            : await f.Workflow.SubmitAsync(f.Pack.Id.ToString(), f.User, Ct);
        Assert.True(result.invalid);
        Assert.Contains("22 Aug 2026", result.error);
        Assert.Contains("31 Aug 2026", result.error);
        Assert.Equal("not_started", f.Pack.Status);
    }

    [Fact]
    public async Task NoConnection_ManualWorkflowControlsReadiness_AndCanSubmitAndClose()
    {
        await using var f = await Fixture.Create();
        var missing = await f.Workflow.SubmitAsync(f.Pack.Id.ToString(), f.User, Ct);
        Assert.Contains("Upload all required documents", missing.error);
        // Accepted manual evidence already travelled through the established document workflow.
        var manualDocumentId = Guid.NewGuid();
        f.Slot.MarkDraft(manualDocumentId);
        f.Slot.Submit(Guid.NewGuid());
        f.Slot.Accept(manualDocumentId);
        await f.Db.SaveChangesAsync(Ct);
        var submitted = await f.Workflow.SubmitAsync(f.Pack.Id.ToString(), f.User, Ct);
        Assert.False(submitted.invalid);
        Assert.Equal("under_review", submitted.pack!.Status);
        var closed = await f.Workflow.CloseAsync(f.Pack.Id.ToString(), f.User, Ct);
        Assert.False(closed.invalid);
        Assert.Equal("closed", closed.pack!.Status);
    }

    [Fact]
    public async Task ExistingEvidence_PreservedAcrossConnectAndDisconnect()
    {
        await using var f = await Fixture.Create();
        var documentId = Guid.NewGuid();
        f.Db.Documents.Add(SecureClientPortal.Backend.Domain.Modules.Documents.Document.CreateUploaded(
            documentId, f.ClientId, f.Pack.Id, "statement.pdf", "bank_statement", f.Slot.Id,
            "application/pdf", 10, "test/statement.pdf", Guid.NewGuid()));
        f.Slot.MarkDraft(documentId);
        await f.Db.SaveChangesAsync(Ct);
        var connected = await f.Banking.ConnectSandboxAsync(f.ClientId, f.User, Ct);
        Assert.Equal(documentId, f.Slot.CurrentDocumentId);
        Assert.Equal("draft", f.Slot.Status);
        Assert.False(f.Slot.IsRequired);
        await f.Banking.DisconnectAsync(connected.Value!.Connections.Single().Id, f.User, Ct);
        Assert.Equal(documentId, f.Slot.CurrentDocumentId);
        Assert.Equal("draft", f.Slot.Status);
        Assert.True(f.Slot.IsRequired);
        Assert.Equal(documentId, (await f.Db.Documents.SingleAsync(Ct)).Id);
        Assert.Equal("test/statement.pdf", (await f.Db.Documents.SingleAsync(Ct)).StorageKey);
    }

    [Fact]
    public async Task LegacyFlag_CannotFakeConnection_OrRemoveManualSlot()
    {
        await using var f = await Fixture.Create();
        var result = await f.Profile.UpdateAsync(f.ClientId,
            new UpdateClientMonthlyPackProfileRequest(null, [], new ClientOperatingProfileInput(BankFeedConnected: true),
                Day(1), true), f.User, Ct);
        Assert.False(result.Forbidden);
        Assert.Equal("include", result.Value!.Recommendations!.Single(x => x.Category == "bank_statement").Decision);
        Assert.Equal("not_started", f.Slot.Status);
        Assert.True(f.Slot.IsRequired);
        Assert.Equal("not_connected", (await f.Status()).Status);
    }

    [Fact]
    public async Task ProfileReconciliation_UsesRealConnection_AndPreservesVisibleSlot()
    {
        await using var f = await Fixture.Create();
        await f.AddBank("test-alpha", (1, 31));
        await f.Profile.ReconcileCurrentPackAsync(f.ClientId, f.User, Ct);
        Assert.Equal("not_applicable", f.Slot.Status);
        Assert.Single(await f.Db.DocumentSlots.ToListAsync(Ct));
    }

    [Fact]
    public async Task AttentionDespiteCoverage_IsNotComplete_AndCannotClose()
    {
        await using var f = await Fixture.Create();
        var bank = await f.AddBank("test-alpha", (1, 31));
        bank.MarkNeedsAttention("Private provider diagnostic");
        await f.Banks.SaveChangesAsync(Ct);
        var status = await f.Status();
        Assert.Equal("needs_attention", status.Status);
        Assert.False(status.IsPeriodComplete);
        Assert.DoesNotContain("Private", status.Message);
        Assert.True((await f.Workflow.CloseAsync(f.Pack.Id.ToString(), f.User, Ct)).invalid);
    }

    [Fact]
    public async Task Providers_RouteSyncAndDisconnectByConnection_AllowDifferentBanks_BlockDuplicateProvider()
    {
        await using var f = await Fixture.Create();
        var bank = await f.AddBank("test-alpha");
        var first = await f.Banking.ConnectSandboxAsync(f.ClientId, f.User, Ct);
        Assert.True(first.Success);
        var duplicate = await f.Banking.ConnectSandboxAsync(f.ClientId, f.User, Ct);
        Assert.False(duplicate.Success);
        await f.Banking.SyncAsync(bank.Id, f.User, Ct);
        Assert.Equal(1, f.Alpha.SyncCount);
        Assert.Equal(0, f.Sandbox.SyncCount);
        await f.Banking.DisconnectAsync(bank.Id, f.User, Ct);
        Assert.Equal(1, f.Alpha.DisconnectCount);
        Assert.Equal("not_applicable", f.Slot.Status);
    }

    [Fact]
    public async Task FailedSync_DoesNotCreateCoverage_OrLeakProviderError()
    {
        await using var f = await Fixture.Create();
        var bank = await f.AddBank("test-alpha");
        f.Alpha.FailSync = true;
        var result = await f.Banking.SyncAsync(bank.Id, f.User, Ct);
        Assert.False(result.Success);
        Assert.DoesNotContain("secret", result.Error);
        Assert.Equal("failed", Assert.Single(await f.Banks.BankSyncRuns.ToListAsync(Ct)).Status);
        Assert.False((await f.Status()).IsPeriodComplete);
    }

    [Fact]
    public async Task LockedHistoricalSlots_AreNotRewritten()
    {
        await using var f = await Fixture.Create();
        f.Pack.MarkUnderReview();
        await f.Db.SaveChangesAsync(Ct);
        await f.AddBank("test-alpha", (1, 31));
        await f.Reconcile();
        Assert.Equal("not_started", f.Slot.Status);
        Assert.True(f.Slot.IsRequired);
    }

    [Fact]
    public async Task ProductionDi_ResolvesBankingAndPackGraph_WithoutMockProvider()
    {
        var services = new ServiceCollection();
        services.AddDbContext<PortalDbContext>(x => x.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddDbContext<BankingDbContext>(x => x.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddOptions<BankingOptions>().Configure(x => x.SandboxEnabled = true);
        services.AddMonthlyPacksModule().AddBankingModule();
        using var root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = root.CreateScope();
        Assert.IsType<BankingService>(scope.ServiceProvider.GetRequiredService<IBankingService>());
        Assert.IsType<BankAwareMonthlyPackService>(scope.ServiceProvider.GetRequiredService<IMonthlyPackService>());
        Assert.IsType<ClientMonthlyPackProfileService>(scope.ServiceProvider.GetRequiredService<IClientMonthlyPackProfileService>());
        Assert.Empty(scope.ServiceProvider.GetServices<IBankDataProvider>());
        Assert.False((await scope.ServiceProvider.GetRequiredService<IBankingService>().ConnectSandboxAsync(null,
            new ClaimsPrincipal(), Ct)).Success);
    }

    private sealed class FixedClock(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }

    [Fact]
    public async Task CompleteBankingOnlyPack_CanSubmitAndClose_WithoutDummyUploads()
    {
        await using var f = await Fixture.Create();
        await f.AddBank("test-alpha", (1, 31));
        var submitted = await f.Workflow.SubmitAsync(f.Pack.Id.ToString(), f.User, Ct);
        Assert.False(submitted.invalid);
        Assert.Equal("under_review", submitted.pack!.Status);
        var closed = await f.Workflow.CloseAsync(f.Pack.Id.ToString(), f.User, Ct);
        Assert.False(closed.invalid);
        Assert.Equal("closed", closed.pack!.Status);
    }

    [Fact]
    public async Task HistoricalLinkedUpload_IsPreserved_EvenWithoutCurrentDocumentPointer()
    {
        await using var f = await Fixture.Create();
        var documentId = Guid.NewGuid();
        f.Db.Documents.Add(SecureClientPortal.Backend.Domain.Modules.Documents.Document.CreateUploaded(
            documentId, f.ClientId, f.Pack.Id, "old.pdf", "bank_statement", f.Slot.Id,
            "application/pdf", 10, "test/old.pdf", Guid.NewGuid()));
        await f.Db.SaveChangesAsync(Ct);
        await f.Banking.ConnectSandboxAsync(f.ClientId, f.User, Ct);
        Assert.Equal("not_started", f.Slot.Status);
        Assert.False(f.Slot.IsRequired);
        Assert.Equal(documentId, (await f.Db.Documents.SingleAsync(Ct)).Id);
        await f.Profile.ReconcileCurrentPackAsync(f.ClientId, f.User, Ct);
        Assert.Equal("not_started", f.Slot.Status);
        Assert.Equal(documentId, (await f.Db.Documents.SingleAsync(Ct)).Id);
    }

    [Fact]
    public async Task MissingProvider_DoesNotUseSandboxToSyncOrDisconnectAnotherBank()
    {
        await using var f = await Fixture.Create();
        var bank = await f.AddBank("unregistered-provider");
        Assert.False((await f.Banking.SyncAsync(bank.Id, f.User, Ct)).Success);
        Assert.False((await f.Banking.DisconnectAsync(bank.Id, f.User, Ct)).Success);
        Assert.Equal(0, f.Sandbox.SyncCount);
        Assert.Equal(0, f.Sandbox.DisconnectCount);
        Assert.Equal("connected", bank.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadinessVerificationFailure_BlocksSubmitAndClose(bool close)
    {
        await using var f = await Fixture.Create();
        var unavailable = new UnavailableBankingService();
        var inner = new MonthlyPackService(f.Db, f.Profile, unavailable);
        var result = close ? await inner.CloseAsync(f.Pack.Id.ToString(), f.User, Ct)
            : await inner.SubmitAsync(f.Pack.Id.ToString(), f.User, Ct);
        Assert.True(result.invalid);
        Assert.Contains("could not be verified", result.error);
        Assert.Equal("not_started", f.Pack.Status);
    }

    private sealed class UnavailableBankingService : IBankingService
    {
        public Task<BankingOperationResult<MonthlyPackBankingStatusDto>> GetMonthlyPackStatusAsync(Guid clientId, int year, int month, ClaimsPrincipal user, CancellationToken ct = default) =>
            Task.FromResult(BankingOperationResult<MonthlyPackBankingStatusDto>.Fail("Internal diagnostic"));
        public Task<BankingOperationResult<bool>> ReconcileMonthlyPackBankSlotAsync(Guid clientId, Guid monthlyPackId, ClaimsPrincipal user, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<BankingOperationResult<BankingOverviewDto>> GetOverviewAsync(Guid? clientId, ClaimsPrincipal user, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<BankingOperationResult<BankingOverviewDto>> ConnectSandboxAsync(Guid? clientId, ClaimsPrincipal user, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<BankingOperationResult<BankingOverviewDto>> SyncAsync(Guid connectionId, ClaimsPrincipal user, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<BankingOperationResult<BankingOverviewDto>> DisconnectAsync(Guid connectionId, ClaimsPrincipal user, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class TestProvider(string name, int month) : IBankDataProvider
    {
        public string Name => name;
        public int SyncCount { get; private set; }
        public int DisconnectCount { get; private set; }
        public bool FailSync { get; set; }
        private IReadOnlyList<ProviderAccount> Accounts => [new("account", "Test bank", "Business", "current", "****1234", "ZAR", 0m, 0m)];
        public Task<ProviderConnectionResult> ConnectAsync(Guid clientId, CancellationToken ct = default) =>
            Task.FromResult(new ProviderConnectionResult(Guid.NewGuid().ToString(), null, "accounts", Accounts, [],
                Day(1, month), Day(DateTime.DaysInMonth(2026, month), month)));
        public Task<ProviderSyncResult> SyncAsync(string externalConnectionId, CancellationToken ct = default)
        {
            SyncCount++;
            if (FailSync) throw new InvalidOperationException("secret provider diagnostic");
            return Task.FromResult(new ProviderSyncResult(Accounts, [], Day(1, month), Day(DateTime.DaysInMonth(2026, month), month)));
        }
        public Task DisconnectAsync(string externalConnectionId, CancellationToken ct = default)
        {
            DisconnectCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public PortalDbContext Db { get; } = new(new DbContextOptionsBuilder<PortalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public BankingDbContext Banks { get; } = new(new DbContextOptionsBuilder<BankingDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public Guid ClientId { get; } = Guid.NewGuid();
        public ClaimsPrincipal User { get; } = new(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()), new Claim(ClaimTypes.Role, "admin"), new Claim("role_scope", "admin")], "test"));
        public MonthlyPack Pack { get; private set; } = null!;
        public DocumentSlot Slot { get; private set; } = null!;
        public BankingService Banking { get; private set; } = null!;
        public ClientMonthlyPackProfileService Profile { get; private set; } = null!;
        public BankAwareMonthlyPackService Workflow { get; private set; } = null!;
        public TestProvider Alpha { get; private set; } = null!;
        public TestProvider Sandbox { get; private set; } = null!;

        public static async Task<Fixture> Create(DateTime? now = null, int month = 8)
        {
            var f = new Fixture();
            f.Db.Clients.Add(Client.Create(f.ClientId, "Banking test", "Company", "Finance", "finance@example.test", ClientStatus.Active));
            f.Pack = MonthlyPack.Create(Guid.NewGuid(), f.ClientId, 2026, month);
            f.Slot = DocumentSlot.Create(Guid.NewGuid(), f.Pack.Id, f.ClientId, "bank_statement", "Bank Statement", true, null);
            f.Db.MonthlyPacks.Add(f.Pack);
            f.Db.DocumentSlots.Add(f.Slot);
            await f.Db.SaveChangesAsync(Ct);
            f.Alpha = new TestProvider("test-alpha", month);
            f.Sandbox = new TestProvider("sandbox", month);
            f.Banking = new BankingService(f.Db, f.Banks, [f.Alpha, f.Sandbox], Options.Create(new BankingOptions { SandboxEnabled = true }), new FixedClock(now ?? Day(16, 9)));
            f.Profile = new ClientMonthlyPackProfileService(f.Db, f.Banks);
            f.Workflow = new BankAwareMonthlyPackService(new MonthlyPackService(f.Db, f.Profile, f.Banking), f.Banking, f.Db);
            return f;
        }
        public Task<BankingOperationResult<bool>> Reconcile() => Banking.ReconcileMonthlyPackBankSlotAsync(ClientId, Pack.Id, User, Ct);
        public async Task<MonthlyPackBankingStatusDto> Status() => (await Banking.GetMonthlyPackStatusAsync(ClientId, Pack.Year, Pack.Month, User, Ct)).Value!;
        public async Task<BankConnection> AddBank(string provider, params (int from, int to)[] ranges)
        {
            var connection = BankConnection.Create(ClientId, provider, Guid.NewGuid().ToString(), Day(1, Pack.Month));
            Banks.BankConnections.Add(connection);
            Banks.BankAccounts.Add(BankAccount.Create(connection.Id, ClientId, "account", "Test bank", "Business", "current", "****1234", "ZAR", 0m, 0m, Day(1, Pack.Month)));
            foreach (var range in ranges)
            {
                var run = BankSyncRun.Start(connection.Id, ClientId, provider, Day(1, Pack.Month));
                run.Complete(0, Day(range.from, Pack.Month), Day(range.to, Pack.Month), Day(range.to, Pack.Month));
                Banks.BankSyncRuns.Add(run);
            }
            await Banks.SaveChangesAsync(Ct);
            return connection;
        }
        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Banks.DisposeAsync();
        }
    }
}
