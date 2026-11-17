using ClearCase.Application.Disputes;
using ClearCase.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;

namespace ClearCase.Infrastructure.Disputes;

/// <summary>SQLite's transaction-level writer lock serializes claims across independent workers.</summary>
public sealed class SqliteOutboxStore(TransactionDbContext context) : IOutboxStore
{
    public async Task<OutboxClaim?> ClaimNextAsync(
        DateTimeOffset now, TimeSpan leaseDuration, CancellationToken cancellationToken)
    {
        if (leaseDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var item = await context.OutboxMessages.AsNoTracking()
            .Where(message => message.PublishedAt == null
                && (message.NextAttemptAt == null || message.NextAttemptAt <= now)
                && (message.LeaseUntilAt == null || message.LeaseUntilAt <= now)
                // A later version of the same case must not overtake an unpublished earlier version.
                && !context.OutboxMessages.Any(previous =>
                    previous.AggregateId == message.AggregateId
                    && previous.AggregateVersion < message.AggregateVersion
                    && previous.PublishedAt == null))
            .OrderBy(message => message.OccurredAt).ThenBy(message => message.EventId)
            .FirstOrDefaultAsync(cancellationToken);
        if (item is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var leaseId = Guid.NewGuid();
        var updated = await context.OutboxMessages
            .Where(message => message.EventId == item.EventId && message.PublishedAt == null
                && (message.LeaseUntilAt == null || message.LeaseUntilAt <= now))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(message => message.LeaseId, leaseId)
                .SetProperty(message => message.LeaseUntilAt, now.Add(leaseDuration))
                .SetProperty(message => message.Attempts, message => message.Attempts + 1),
                cancellationToken);
        if (updated != 1)
            throw new InvalidOperationException("Outbox lease claim conflicted with another writer.");
        await transaction.CommitAsync(cancellationToken);
        var message = new OutboxEvent(item.EventId, item.AggregateId,
            item.AggregateVersion, item.EventType, item.EventVersion, item.CorrelationId, item.Payload,
            item.OccurredAt, checked(item.Attempts + 1));
        return new OutboxClaim(message, leaseId);
    }

    public async Task<bool> MarkPublishedAsync(
        Guid eventId, Guid leaseId, DateTimeOffset publishedAt, CancellationToken cancellationToken) =>
        await context.OutboxMessages.Where(item =>
                item.EventId == eventId && item.LeaseId == leaseId && item.PublishedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.PublishedAt, publishedAt)
                .SetProperty(item => item.LeaseId, (Guid?)null)
                .SetProperty(item => item.LeaseUntilAt, (DateTimeOffset?)null)
                .SetProperty(item => item.NextAttemptAt, (DateTimeOffset?)null), cancellationToken) == 1;

    public async Task<bool> ScheduleRetryAsync(
        Guid eventId, Guid leaseId, DateTimeOffset nextAttemptAt, CancellationToken cancellationToken) =>
        await context.OutboxMessages.Where(item =>
                item.EventId == eventId && item.LeaseId == leaseId && item.PublishedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.LeaseId, (Guid?)null)
                .SetProperty(item => item.LeaseUntilAt, (DateTimeOffset?)null)
                .SetProperty(item => item.NextAttemptAt, nextAttemptAt), cancellationToken) == 1;
}
