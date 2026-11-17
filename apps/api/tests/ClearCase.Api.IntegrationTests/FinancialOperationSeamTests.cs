using ClearCase.Application.AgentDisputes;
using ClearCase.Application.Disputes;
using ClearCase.Domain.Disputes;
using ClearCase.Infrastructure.Disputes;
using ClearCase.Infrastructure.Transactions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using ClearCase.Application.Chargebacks;
using ClearCase.Application.Transactions;

namespace ClearCase.Api.IntegrationTests;

/// <summary>No database or payment network is touched by these orchestration contract tests.</summary>
public sealed class FinancialOperationSeamTests
{
    [Fact]
    public async Task RecoverRecordedResult_NeverCallsProviderAgain()
    {
        var store = new StubIntents
        {
            Claim = CreateClaim(requiresProviderExecution: false, outcome: "Confirmed")
        };
        var gateway = new StubStrategy();
        var apply = new StubApplier();
        var coordinator = NewCoordinator(store, gateway, apply);

        Assert.True(await coordinator.RecoverNextAsync(CancellationToken.None));
        Assert.Equal(0, gateway.Calls);
        Assert.Equal(1, apply.Calls);
        Assert.Equal("Confirmed", apply.LastOutcome);
        Assert.Equal(1, store.Completed);
    }

    [Fact]
    public async Task ProviderTimeout_IsRecordedAsUnknownWithoutRetry()
    {
        var store = new StubIntents { Claim = CreateClaim(true) };
        var gateway = new StubStrategy { ThrowTimeout = true };
        var apply = new StubApplier();

        Assert.True(await NewCoordinator(store, gateway, apply).RecoverNextAsync(CancellationToken.None));

        Assert.Equal(1, gateway.Calls);
        Assert.Equal("Unknown", store.Recorded);
        Assert.Equal("Unknown", apply.LastOutcome);
        Assert.Equal(1, store.Completed);
    }

    [Fact]
    public async Task LostLease_NeverAppliesProviderResult()
    {
        var store = new StubIntents { Claim = CreateClaim(true), AcceptOutcome = false };
        var gateway = new StubStrategy();
        var apply = new StubApplier();

        Assert.True(await NewCoordinator(store, gateway, apply).RecoverNextAsync(CancellationToken.None));

        Assert.Equal(1, gateway.Calls);
        Assert.Equal(0, apply.Calls);
        Assert.Equal(0, store.Completed);
    }

    [Fact]
    public async Task UnavailableProvider_FailsClosedBeforeScheduling()
    {
        var store = new StubIntents { Claim = CreateClaim(true) };
        var gateway = new StubStrategy { IsAvailable = false };
        var coordinator = NewCoordinator(store, gateway, new StubApplier());

        await Assert.ThrowsAsync<DisputeProcessingUnavailableException>(() =>
            coordinator.ScheduleAsync(new FinancialOperationRequest(
                Guid.NewGuid(), Guid.NewGuid(), FinancialOperationKind.DebitReversal,
                null, null, "unit-test"), CancellationToken.None));
        Assert.Equal(0, store.Schedules);
    }

    [Fact]
    public void ScopedPorts_ResolveOneCoordinatorPerScope()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IDebitOrderProcessingGateway, UnavailableDisputeProcessingGateway>();
        services.AddSingleton<IBankChargeAdjustmentGateway, UnavailableDisputeProcessingGateway>();
        services.AddSingleton<ICardNetworkGateway, UnavailableDisputeProcessingGateway>();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Persistence:DatabasePath"] = Path.Combine(Path.GetTempPath(), "clearcase-di-contract.db"),
                ["Persistence:BusyTimeoutSeconds"] = "5"
            }).Build());
        services.AddTransactionPersistence(Path.GetTempPath(), null, development: false);
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        var exec = first.ServiceProvider.GetRequiredService<IFinancialOperationExecutor>();
        var recovery = first.ServiceProvider.GetRequiredService<IFinancialOperationRecovery>();
        var legacy = first.ServiceProvider.GetRequiredService<IFinancialOperationCoordinator>();
        var other = second.ServiceProvider.GetRequiredService<IFinancialOperationExecutor>();

        Assert.Same(exec, recovery);
        Assert.Same(exec, legacy);
        Assert.NotSame(exec, other);
    }

    private static FinancialIntentClaim CreateClaim(bool requiresProviderExecution, string? outcome = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), FinancialOperationKind.DebitReversal,
            "OP-STABLE", 2, null, "test-correlation", Guid.NewGuid(),
            requiresProviderExecution, outcome);

    private static SqliteFinancialOperationCoordinator NewCoordinator(
        StubIntents intents, StubStrategy gateway, StubApplier applier) =>
        new(intents, applier, new UnusedDisputes(), [gateway],
            NullLogger<SqliteFinancialOperationCoordinator>.Instance);

    private sealed class StubStrategy : IFinancialOperationStrategy
    {
        public bool IsAvailable { get; set; } = true;
        public bool ThrowTimeout { get; set; }
        public int Calls { get; private set; }
        public bool Supports(FinancialOperationKind kind) => kind == FinancialOperationKind.DebitReversal;
        public Task<string> ExecuteAsync(FinancialOperationExecution operation, CancellationToken ct)
        {
            Calls++;
            if (ThrowTimeout) throw new TimeoutException("Fake ambiguous provider timeout");
            return Task.FromResult("Confirmed");
        }
    }

    private sealed class StubApplier : IRecordedFinancialOutcomeApplier
    {
        public int Calls { get; private set; }
        public string? LastOutcome { get; private set; }
        public Task ApplyAsync(FinancialIntentClaim claim, string outcome, CancellationToken ct)
        {
            Calls++;
            LastOutcome = outcome;
            return Task.CompletedTask;
        }
    }

    private sealed class StubIntents : IFinancialIntentStore
    {
        public FinancialIntentClaim? Claim { get; set; }
        public bool AcceptOutcome { get; set; } = true;
        public string? Recorded { get; private set; }
        public int Completed { get; private set; }
        public int Schedules { get; private set; }
        public Task<Guid?> ScheduleAsync(FinancialOperationRequest request, CancellationToken ct)
        {
            Schedules++;
            return Task.FromResult<Guid?>(Claim?.Id);
        }
        public Task<Guid?> FindRecoverableAsync(CancellationToken ct) => Task.FromResult<Guid?>(Claim?.Id);
        public Task<FinancialIntentClaim?> ClaimAsync(Guid id, CancellationToken ct) => Task.FromResult(Claim);
        public Task<bool> RecordOutcomeAsync(FinancialIntentClaim claim, string outcome, CancellationToken ct)
        {
            Recorded = outcome;
            return Task.FromResult(AcceptOutcome);
        }
        public Task<bool> WasAppliedAsync(FinancialIntentClaim claim, CancellationToken ct) =>
            Task.FromResult(false);
        public Task CompleteAsync(FinancialIntentClaim claim, CancellationToken ct)
        {
            Completed++;
            return Task.CompletedTask;
        }
    }

    private sealed class UnusedDisputes : IDisputeStore
    {
        public Task<DisputeListPage> ReadPageAsync(Guid customerId, ListDisputesQuery query, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<bool> HasOpenDisputeAsync(Guid customerId, Guid transactionId, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<DisputeAddResult> AddAsync(Dispute dispute, DisputeSubmission? submission,
            CancellationToken ct, OutboxMetadata? outboxMetadata = null) => throw new NotSupportedException();
        public Task<StoredDisputeSubmission?> FindSubmissionAsync(Guid customerId, string key, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<DisputeView?> ReadAsync(Guid customerId, Guid disputeId, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
