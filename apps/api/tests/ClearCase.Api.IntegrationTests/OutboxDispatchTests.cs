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
        Assert.Equal(1, await first.OutboxMessages.AsNoTracking().SingleAsync()
            .ContinueWith(task => task.Result.Attempts));
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
