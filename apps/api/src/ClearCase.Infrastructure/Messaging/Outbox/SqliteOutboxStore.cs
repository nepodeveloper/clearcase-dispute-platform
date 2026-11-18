using ClearCase.Application.Disputes;
using ClearCase.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;

namespace ClearCase.Infrastructure.Disputes;

/// <summary>SQLite's transaction-level writer lock serializes claims across independent workers.</summary>
public sealed class SqliteOutboxStore(TransactionDbContext context) : IOutboxStore, IOutboxRecoveryStore
{
    public async Task<OutboxClaim?> ClaimNextAsync(
        DateTimeOffset now, TimeSpan leaseDuration, CancellationToken cancellationToken)
    {
        if (leaseDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var item = await context.OutboxMessages.AsNoTracking()
            .Where(message => message.PublishedAt == null
                && message.ParkedAt == null
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
                && message.ParkedAt == null
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

    public async Task<bool> ParkAsync(
        Guid eventId, Guid leaseId, DateTimeOffset parkedAt,
        string reasonCode, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);
        return await context.OutboxMessages.Where(message =>
                message.EventId == eventId && message.LeaseId == leaseId
                && message.PublishedAt == null && message.ParkedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(message => message.ParkedAt, parkedAt)
                .SetProperty(message => message.ParkReasonCode, reasonCode)
                .SetProperty(message => message.LeaseId, (Guid?)null)
                .SetProperty(message => message.LeaseUntilAt, (DateTimeOffset?)null)
                .SetProperty(message => message.NextAttemptAt, (DateTimeOffset?)null),
                cancellationToken) == 1;
    }

    public async Task<IReadOnlyList<ParkedOutboxItem>> ListParkedAsync(
        int maxItems, CancellationToken cancellationToken)
    {
        if (maxItems is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(maxItems));
        return await context.OutboxMessages.AsNoTracking()
            .Where(message => message.ParkedAt != null && message.PublishedAt == null)
            .OrderBy(message => message.ParkedAt).ThenBy(message => message.EventId)
            .Take(maxItems)
            .Select(message => new ParkedOutboxItem(message.EventId, message.AggregateId,
                message.AggregateVersion, message.EventType, message.Attempts,
                message.ParkedAt!.Value, message.ParkReasonCode ?? "delivery.unknown"))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<OutboxRequeueOutcome> RequeueAsync(
        Guid eventId, Guid employeeId, string reason,
        DateTimeOffset occurredAt, CancellationToken cancellationToken)
    {
        if (employeeId == Guid.Empty || reason.Length is < 15 or > 500
            || string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Provide a trusted employee and meaningful reason.");
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var exists = await context.OutboxMessages.AsNoTracking()
            .AnyAsync(message => message.EventId == eventId, cancellationToken);
        if (!exists) return OutboxRequeueOutcome.NotFound;
        var updated = await context.OutboxMessages
            .Where(message => message.EventId == eventId
                && message.ParkedAt != null
                && message.PublishedAt == null && message.LeaseId == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(message => message.ParkedAt, (DateTimeOffset?)null)
                .SetProperty(message => message.ParkReasonCode, (string?)null)
                .SetProperty(message => message.NextAttemptAt, (DateTimeOffset?)null)
                .SetProperty(message => message.Attempts, 0),
                cancellationToken);
        if (updated != 1) return OutboxRequeueOutcome.NotParked;
        context.Set<PersistedOutboxRecoveryAudit>().Add(new PersistedOutboxRecoveryAudit
        {
            Id = Guid.NewGuid(), EventId = eventId, EmployeeId = employeeId,
            Reason = reason.Trim(), OccurredAt = occurredAt
        });
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return OutboxRequeueOutcome.Requeued;
    }

    public async Task<bool> ScheduleRetryAsync(
        Guid eventId, Guid leaseId, DateTimeOffset nextAttemptAt, CancellationToken cancellationToken) =>
        await context.OutboxMessages.Where(item =>
                item.EventId == eventId && item.LeaseId == leaseId && item.PublishedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.LeaseId, (Guid?)null)
                .SetProperty(item => item.LeaseUntilAt, (DateTimeOffset?)null)
                .SetProperty(item => item.NextAttemptAt, nextAttemptAt), cancellationToken) == 1;
}
