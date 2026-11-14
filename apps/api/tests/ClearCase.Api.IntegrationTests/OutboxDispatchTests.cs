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
    public async Task Dispatch_RetriesSameEventAfterFailureAndMarksPublishedOnlyAfterAcknowledgement()
    {
        await using var database = new TemporaryDatabase();
        await using (var setup = database.CreateContext())
        {
            await setup.Database.MigrateAsync();
            await SyntheticTransactionSeeder.SeedAsync(setup, CancellationToken.None);
            var dispute = NewDispute();
            Assert.Equal(DisputeAddResult.Added,
                await new SqliteDisputeStore(setup).AddAsync(dispute, CancellationToken.None));
        }

        var publisher = new FailOncePublisher();
        await using var context = database.CreateContext();
        var store = new SqliteOutboxStore(context);
        var dispatcher = new OutboxDispatcher(store, publisher,
            new FixedTimeProvider(OccurredAt.AddMinutes(1)), NullLogger<OutboxDispatcher>.Instance);

        Assert.False(await dispatcher.DispatchNextAsync(CancellationToken.None));
        context.ChangeTracker.Clear();
        var pending = await context.OutboxMessages.AsNoTracking().SingleAsync();
        Assert.Null(pending.PublishedAt);
        Assert.Equal(1, pending.Attempts);

        Assert.True(await dispatcher.DispatchNextAsync(CancellationToken.None));
        context.ChangeTracker.Clear();
        var published = await context.OutboxMessages.AsNoTracking().SingleAsync();
        Assert.Equal(OccurredAt.AddMinutes(1), published.PublishedAt);
        Assert.Equal(2, published.Attempts);
        Assert.Equal(2, publisher.Events.Count);
        Assert.Equal(publisher.Events.First().EventId, publisher.Events.Last().EventId);
        Assert.Null(await store.ReadNextAsync(CancellationToken.None));
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

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
