using ClearCase.Application.Disputes;
using ClearCase.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;

namespace ClearCase.Infrastructure.Disputes;

public sealed class SqliteOutboxStore(TransactionDbContext context) : IOutboxStore
{
    public async Task<OutboxEvent?> ReadNextAsync(CancellationToken cancellationToken)
    {
        var item = await context.OutboxMessages.AsNoTracking()
            .Where(message => message.PublishedAt == null)
            .OrderBy(message => message.OccurredAt)
            .ThenBy(message => message.EventId)
            .FirstOrDefaultAsync(cancellationToken);
        return item is null ? null : new OutboxEvent(item.EventId, item.AggregateId,
            item.AggregateVersion, item.EventType, item.EventVersion, item.CorrelationId, item.Payload,
            item.OccurredAt, item.Attempts);
    }

    public async Task RecordAttemptAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var message = await context.OutboxMessages.SingleAsync(
            item => item.EventId == eventId && item.PublishedAt == null, cancellationToken);
        message.Attempts = checked(message.Attempts + 1);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkPublishedAsync(
        Guid eventId, DateTimeOffset publishedAt, CancellationToken cancellationToken)
    {
        var message = await context.OutboxMessages.SingleAsync(
            item => item.EventId == eventId && item.PublishedAt == null, cancellationToken);
        message.PublishedAt = publishedAt;
        await context.SaveChangesAsync(cancellationToken);
    }
}