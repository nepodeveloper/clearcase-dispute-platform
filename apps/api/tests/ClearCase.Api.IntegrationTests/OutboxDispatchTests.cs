using System.Collections.Concurrent;
using ClearCase.Application.Disputes;
using ClearCase.Domain.DisputeEligibility;
using ClearCase.Domain.Disputes;
using ClearCase.Domain.Transactions;
using ClearCase.Infrastructure.Disputes;
using ClearCase.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClearCase.Api.IntegrationTests;

public sealed class OutboxDispatchTests
{
    private static readonly Guid CustomerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TransactionId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset OccurredAt = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Dispatch_RetriesWithBackoffAndAcknowledgesOnlyAfterSuccessfulPublish()
    {
        await using var database = await SeedAsync();
        var publisher = new FailOncePublisher();
        var clock = new MutableTimeProvider(OccurredAt.AddMinutes(1));
        await using var context = database.CreateContext();
        var store = new SqliteOutboxStore(context);
        var dispatcher = new OutboxDispatcher(store, publisher, clock, NullLogger<OutboxDispatcher>.Instance);

        Assert.False(await dispatcher.DispatchNextAsync(default));
        var pending = await context.OutboxMessages.AsNoTracking().SingleAsync();
        Assert.Null(pending.PublishedAt);
        Assert.Null(pending.LeaseId);
        Assert.Null(pending.LeaseUntilAt);
        Assert.Equal(clock.GetUtcNow().AddSeconds(2), pending.NextAttemptAt);
        Assert.Equal(1, pending.Attempts);
        Assert.False(await dispatcher.DispatchNextAsync(default));
        Assert.Single(publisher.Events);

        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.True(await dispatcher.DispatchNextAsync(default));
        var published = await context.OutboxMessages.AsNoTracking().SingleAsync();
        Assert.Equal(clock.GetUtcNow(), published.PublishedAt);
        Assert.Null(published.LeaseId);
        Assert.Null(published.NextAttemptAt);
        Assert.Equal(2, published.Attempts);
        Assert.Equal(2, publisher.Events.Count);
        Assert.Equal(publisher.Events.First().EventId, publisher.Events.Last().EventId);
        Assert.Null(await store.ClaimNextAsync(clock.GetUtcNow(), TimeSpan.FromMinutes(2), default));
    }

    [Fact]
    public async Task TwoWorkersCannotOwnTheSameUnexpiredLease()
    {
        await using var database = await SeedAsync();
        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        var now = OccurredAt.AddMinutes(1);
        var results = await Task.WhenAll(
            new SqliteOutboxStore(first).ClaimNextAsync(now, TimeSpan.FromMinutes(2), default),
            new SqliteOutboxStore(second).ClaimNextAsync(now, TimeSpan.FromMinutes(2), default));
        var claim = Assert.Single(results.OfType<OutboxClaim>());
        var stored = await first.OutboxMessages.AsNoTracking().SingleAsync();
        Assert.Equal(1, stored.Attempts);
        Assert.False(await new SqliteOutboxStore(first).MarkPublishedAsync(
            claim.Message.EventId, Guid.NewGuid(), now, default));
        Assert.True(await new SqliteOutboxStore(second).MarkPublishedAsync(
            claim.Message.EventId, claim.LeaseId, now, default));
    }

    [Fact]
    public async Task RestartRecoversExpiredLeaseAndFencesStaleAcknowledgements()
    {
        await using var database = await SeedAsync();
        var now = OccurredAt.AddMinutes(1);
        OutboxClaim abandoned;
        await using (var crashedContext = database.CreateContext())
        {
            abandoned = (await new SqliteOutboxStore(crashedContext)
                .ClaimNextAsync(now, TimeSpan.FromMinutes(2), default))!;
            Assert.NotNull(abandoned);
        }
        // The previous worker published or crashed; only a new lease can ack after expiry.
        await using var restartedContext = database.CreateContext();
        var restartedStore = new SqliteOutboxStore(restartedContext);
        Assert.Null(await restartedStore.ClaimNextAsync(now.AddMinutes(1),
            TimeSpan.FromMinutes(2), default));
        var recovered = (await restartedStore.ClaimNextAsync(now.AddMinutes(2).AddTicks(1),
            TimeSpan.FromMinutes(2), default))!;
        Assert.Equal(abandoned.Message.EventId, recovered.Message.EventId);
        Assert.NotEqual(abandoned.LeaseId, recovered.LeaseId);
        Assert.Equal(2, recovered.Message.Attempts);
        Assert.False(await restartedStore.MarkPublishedAsync(
            abandoned.Message.EventId, abandoned.LeaseId, now.AddMinutes(3), default));
        Assert.True(await restartedStore.MarkPublishedAsync(
            recovered.Message.EventId, recovered.LeaseId, now.AddMinutes(3), default));
        Assert.NotNull((await restartedContext.OutboxMessages.AsNoTracking().SingleAsync()).PublishedAt);
    }

    [Fact]
    public async Task ExhaustedMessageIsParkedAndCanBeAuditedRequeuedWithoutChangingEventIdentity()
    {
        await using var database = await SeedAsync();
        var clock = new MutableTimeProvider(OccurredAt.AddMinutes(1));
        await using var context = database.CreateContext();
        var store = new SqliteOutboxStore(context);
        var failed = new AlwaysFailPublisher();
        var dispatcher = new OutboxDispatcher(store, failed, clock, NullLogger<OutboxDispatcher>.Instance);
        var eventId = (await context.OutboxMessages.AsNoTracking().SingleAsync()).EventId;

        for (var attempt = 1; attempt <= OutboxDispatcher.MaximumDeliveryAttempts; attempt++)
        {
            Assert.False(await dispatcher.DispatchNextAsync(default));
            clock.Advance(TimeSpan.FromMinutes(6));
        }
        Assert.Equal(OutboxDispatcher.MaximumDeliveryAttempts, failed.Attempts);
        var parked = await context.OutboxMessages.AsNoTracking().SingleAsync();
        Assert.Equal(eventId, parked.EventId);
        Assert.NotNull(parked.ParkedAt);
        Assert.Equal("delivery.attemptsExhausted", parked.ParkReasonCode);
        Assert.Null(parked.PublishedAt);
        Assert.Null(parked.LeaseId);
        Assert.Null(parked.NextAttemptAt);
        Assert.False(await dispatcher.DispatchNextAsync(default));
        Assert.Single(await store.ListParkedAsync(50, default));

        var employee = Guid.NewGuid();
        Assert.Equal(OutboxRequeueOutcome.Requeued, await store.RequeueAsync(
            eventId, employee, "Reviewed broker state; safe to replay this mock event.",
            clock.GetUtcNow(), default));
        Assert.Equal(OutboxRequeueOutcome.NotParked, await store.RequeueAsync(
            eventId, employee, "Duplicate operational recovery must be blocked.",
            clock.GetUtcNow(), default));
        var audit = await context.Set<PersistedOutboxRecoveryAudit>().AsNoTracking().SingleAsync();
        Assert.Equal(employee, audit.EmployeeId);
        Assert.Equal(eventId, audit.EventId);
        Assert.Equal(0, (await context.OutboxMessages.AsNoTracking().SingleAsync()).Attempts);
        Assert.Empty(await store.ListParkedAsync(50, default));

        var recovered = new OutboxDispatcher(store,
            new LocalOutboxPublisher(NullLogger<LocalOutboxPublisher>.Instance),
            clock, NullLogger<OutboxDispatcher>.Instance);
        Assert.True(await recovered.DispatchNextAsync(default));
        var finished = await context.OutboxMessages.AsNoTracking().SingleAsync();
        Assert.Equal(eventId, finished.EventId);
        Assert.Equal(1, finished.Attempts);
        Assert.NotNull(finished.PublishedAt);
        Assert.Single(await context.Set<PersistedOutboxRecoveryAudit>().ToArrayAsync());
    }

    [Fact]
    public async Task ParkingRequiresCurrentLeaseAndFencesStaleAcknowledgments()
    {
        await using var database = await SeedAsync();
        await using var context = database.CreateContext();
        var store = new SqliteOutboxStore(context);
        var now = OccurredAt.AddMinutes(1);
        var claim = (await store.ClaimNextAsync(now, TimeSpan.FromMinutes(2), default))!;
        Assert.NotNull(claim);
        Assert.False(await store.ParkAsync(claim.Message.EventId, Guid.NewGuid(),
            now, "delivery.attemptsExhausted", default));
        Assert.True(await store.ParkAsync(claim.Message.EventId, claim.LeaseId,
            now, "delivery.attemptsExhausted", default));
        Assert.False(await store.MarkPublishedAsync(claim.Message.EventId,
            claim.LeaseId, now, default));
        Assert.Null(await store.ClaimNextAsync(now.AddDays(1), TimeSpan.FromMinutes(2), default));
    }

    private sealed class AlwaysFailPublisher : IOutboxPublisher
    {
        public int Attempts { get; private set; }
        public Task PublishAsync(OutboxEvent message, CancellationToken cancellationToken)
        {
            Attempts++;
            throw new InvalidOperationException("Synthetic local outbox failure.");
        }
    }

    private static async Task<TemporaryDatabase> SeedAsync()
    {
        var database = new TemporaryDatabase();
        await using var setup = database.CreateContext();
        await setup.Database.MigrateAsync();
        await SyntheticTransactionSeeder.SeedAsync(setup, default);
        Assert.Equal(DisputeAddResult.Added,
            await new SqliteDisputeStore(setup).AddAsync(NewDispute(), default));
        return database;
    }

    private static Dispute NewDispute()
    {
        var transaction = new DisputeTransaction(TransactionId, TransactionCategory.CardPurchase, 25m, "ZAR",
            OccurredAt.AddDays(-1), TransactionStatus.Posted, true, true, null, null, null, null);
        var eligibility = new DisputeEligibilityResult(DisputeEligibilityOutcome.EligibleToSubmit, [],
            "Eligible for review.", "test-v1", OccurredAt);
        return Dispute.Submit(Guid.NewGuid(), "DSP-OUTBOX0001", CustomerId, transaction,
            "unrecognisedTransaction", "I did not make this purchase at the store.", eligibility, Guid.NewGuid());
    }

    private sealed class FailOncePublisher : IOutboxPublisher
    {
        private int _calls;
        public ConcurrentQueue<OutboxEvent> Events { get; } = new();

        public Task PublishAsync(OutboxEvent message, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Events.Enqueue(message);
            if (Interlocked.Increment(ref _calls) == 1)
                throw new InvalidOperationException("Simulated local publisher failure.");
            return Task.CompletedTask;
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset instant) : TimeProvider
    {
        private DateTimeOffset _now = instant;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now = _now.Add(duration);
    }
}
