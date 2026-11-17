using ClearCase.Application.Chargebacks;
using ClearCase.Application.Disputes;
using ClearCase.Application.Transactions;
using ClearCase.Domain.DisputeEligibility;
using ClearCase.Domain.Disputes;
using ClearCase.Domain.Transactions;
using ClearCase.Infrastructure.Chargebacks;
using ClearCase.Infrastructure.Disputes;
using ClearCase.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClearCase.Api.IntegrationTests;

public sealed class FinancialOperationRecoveryTests
{
    private static readonly Guid CustomerId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task PersistedIntentSurvivesRestartAndExecutesOutsideTheCaseTransaction()
    {
        await using var db = new TemporaryDatabase();
        Guid disputeId;
        await using (var first = db.CreateContext())
        {
            await first.Database.MigrateAsync();
            disputeId = await SeedRequestedReversal(first);
            var coordinator = NewCoordinator(first, new RecordingDebitGateway(first));
            var intentId = await coordinator.ScheduleAsync(Request(disputeId), default);
            Assert.NotNull(intentId);
            var scheduled = await first.FinancialOperationIntents.AsNoTracking().SingleAsync();
            Assert.Equal(FinancialOperationStage.Pending, scheduled.Stage);
            Assert.Null(scheduled.Outcome);
            Assert.Equal(2, (await first.Disputes.AsNoTracking().SingleAsync()).Version);
            Assert.Equal(2, await first.DisputeEvents.CountAsync());
        }
        await using (var restarted = db.CreateContext())
        {
            var gateway = new RecordingDebitGateway(restarted);
            Assert.True(await NewCoordinator(restarted, gateway).RecoverNextAsync(default));
            Assert.Equal(1, gateway.Calls);
            Assert.True(gateway.ExecutedOutsideTransaction);
            var intent = await restarted.FinancialOperationIntents.AsNoTracking().SingleAsync();
            Assert.Equal(FinancialOperationStage.Completed, intent.Stage);
            Assert.Equal("Confirmed", intent.Outcome);
            Assert.Equal(1, intent.Attempts);
            var changed = await restarted.Disputes.AsNoTracking().SingleAsync();
            Assert.Equal(DisputeFinancialStatus.Confirmed, changed.FinancialStatus);
            Assert.Equal(DisputeCaseStatus.ReversalConfirmed, changed.CaseStatus);
            Assert.Equal(3, changed.Version);
            Assert.Equal(3, await restarted.DisputeEvents.CountAsync());
            Assert.Single(await restarted.OutboxMessages.ToArrayAsync());
            Assert.False(await NewCoordinator(restarted, gateway).RecoverNextAsync(default));
        }
    }

    [Fact]
    public async Task RepeatedFinancialIntentIsAConflictNotASecondSuccessfulCommand()
    {
        await using var db = new TemporaryDatabase();
        await using var context = db.CreateContext();
        await context.Database.MigrateAsync();
        var disputeId = await SeedRequestedReversal(context);
        var coordinator = NewCoordinator(context, new RecordingDebitGateway(context));
        var original = await coordinator.ScheduleAsync(Request(disputeId), default);
        Assert.NotNull(original);
        await Assert.ThrowsAsync<DisputeConcurrencyConflictException>(() =>
            coordinator.ScheduleAsync(Request(disputeId), default));
        Assert.Single(await context.FinancialOperationIntents.AsNoTracking().ToArrayAsync());
        Assert.Equal(FinancialOperationStage.Pending,
            (await context.FinancialOperationIntents.AsNoTracking().SingleAsync()).Stage);
    }

    [Theory]
    [InlineData(FinancialOperationStage.Dispatched, null, "Unknown", DisputeFinancialStatus.OutcomeUnknown)]
    [InlineData(FinancialOperationStage.ResultRecorded, "Confirmed", "Confirmed", DisputeFinancialStatus.Confirmed)]
    public async Task RecoveryNeverResendsAmbiguousOrRecordedOperations(
        FinancialOperationStage interruptedStage, string? storedResult, string expectedResult,
        DisputeFinancialStatus expectedFinancial)
    {
        await using var db = new TemporaryDatabase();
        await using (var first = db.CreateContext())
        {
            await first.Database.MigrateAsync();
            var disputeId = await SeedRequestedReversal(first);
            await NewCoordinator(first, new RecordingDebitGateway(first))
                .ScheduleAsync(Request(disputeId), default);
            var scheduled = await first.FinancialOperationIntents.SingleAsync();
            scheduled.Stage = interruptedStage;
            scheduled.Outcome = storedResult;
            scheduled.LeaseId = Guid.NewGuid();
            scheduled.LeaseUntilAt = DateTimeOffset.UtcNow.AddMinutes(-5);
            scheduled.Attempts = 1;
            await first.SaveChangesAsync();
        }
        await using var restarted = db.CreateContext();
        var gateway = new RecordingDebitGateway(restarted);
        Assert.True(await NewCoordinator(restarted, gateway).RecoverNextAsync(default));
        Assert.Equal(0, gateway.Calls);
        var recovered = await restarted.FinancialOperationIntents.AsNoTracking().SingleAsync();
        Assert.Equal(FinancialOperationStage.Completed, recovered.Stage);
        Assert.Equal(expectedResult, recovered.Outcome);
        var dispute = await restarted.Disputes.AsNoTracking().SingleAsync();
        Assert.Equal(expectedFinancial, dispute.FinancialStatus);
        Assert.Equal(3, dispute.Version);
        Assert.Equal(3, await restarted.DisputeEvents.CountAsync());
        Assert.Single(await restarted.OutboxMessages.ToArrayAsync());
    }

    private static FinancialOperationRequest Request(Guid disputeId) =>
        new(CustomerId, disputeId, FinancialOperationKind.DebitReversal,
            2, null, "financial-operation-recovery-test");

    private static SqliteFinancialOperationCoordinator NewCoordinator(
        TransactionDbContext context, IDebitOrderProcessingGateway gateway)
    {
        var disputes = new SqliteDisputeStore(context);
        return new SqliteFinancialOperationCoordinator(context, disputes, disputes,
            new SqliteBankChargeProcessingStore(context, disputes),
            new SqliteChargebackProcessingStore(context, disputes),
            gateway, new MockBankChargeAdjustmentGateway(), new MockCardNetworkGateway(),
            TimeProvider.System, NullLogger<SqliteFinancialOperationCoordinator>.Instance);
    }

    private static async Task<Guid> SeedRequestedReversal(TransactionDbContext db)
    {
        var disputeId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.Transactions.Add(new PersistedTransaction
        {
            Id = transactionId, CustomerId = CustomerId,
            Type = TransactionType.DebitOrder,
            Description = "Synthetic debit order for recovery verification",
            AmountMinorUnits = 5200, Currency = "ZAR", TransactionDate = now.AddDays(-1),
            Status = TransactionStatus.Posted, IsDisputable = true, IsDebit = true,
            HasRequiredDisputeInformation = true
        });
        db.Disputes.Add(new PersistedDispute
        {
            Id = disputeId, Reference = "DSP-FIN-" + disputeId.ToString("N"),
            CustomerId = CustomerId, TransactionId = transactionId,
            Category = TransactionCategory.DebitOrder,
            ReasonCode = "unauthorisedDebitOrder",
            Description = "Synthetic recovery verification dispute",
            CaseStatus = DisputeCaseStatus.ReversalRequested,
            DecisionStatus = DisputeDecisionStatus.Approved,
            FinancialStatus = DisputeFinancialStatus.Requested,
            EligibilityOutcome = DisputeEligibilityOutcome.EligibleToSubmit,
            EligibilityPolicyVersion = "test-v1", Version = 2, CreatedAt = now,
            ReversalOperationReference = $"MOCK-DO-{disputeId:N}",
            Events =
            [
                new PersistedDisputeEvent
                {
                    Id = Guid.NewGuid(), DisputeId = disputeId, SequenceNumber = 1,
                    EventType = "DisputeSubmitted", CaseStatus = DisputeCaseStatus.Submitted,
                    PublicDescription = "Your dispute was submitted.", OccurredAt = now.AddMinutes(-1)
                },
                new PersistedDisputeEvent
                {
                    Id = Guid.NewGuid(), DisputeId = disputeId, SequenceNumber = 2,
                    EventType = "DebitOrderRequestReversal", CaseStatus = DisputeCaseStatus.ReversalRequested,
                    PublicDescription = "A simulated reversal was requested.", OccurredAt = now
                }
            ]
        });
        await db.SaveChangesAsync();
        return disputeId;
    }

    private sealed class RecordingDebitGateway(TransactionDbContext context) : IDebitOrderProcessingGateway
    {
        public int Calls { get; private set; }
        public bool ExecutedOutsideTransaction { get; private set; }
        public Task<DebitOrderGatewayOutcome> ReverseAsync(
            string operationReference, CancellationToken cancellationToken)
        {
            Calls++;
            ExecutedOutsideTransaction = context.Database.CurrentTransaction is null;
            return Task.FromResult(DebitOrderGatewayOutcome.Confirmed);
        }
        public Task<DebitOrderGatewayOutcome> GetReversalStatusAsync(
            string operationReference, CancellationToken cancellationToken) =>
            Task.FromResult(DebitOrderGatewayOutcome.Unknown);
    }
}
