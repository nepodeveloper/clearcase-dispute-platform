using ClearCase.Application.Notifications;
using ClearCase.Infrastructure.Disputes;
using ClearCase.Infrastructure.Transactions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ClearCase.Infrastructure.Notifications;

internal static class NotificationPersistenceModel
{
    public static void Configure(ModelBuilder builder)
    {
        var marker = builder.Entity<PersistedNotificationReadMarker>();
        marker.ToTable("NotificationReadMarkers");
        marker.HasKey(item => item.CustomerId);
        marker.Property(item => item.ReadThrough).HasConversion(
            value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
    }
}

public sealed class PersistedNotificationReadMarker
{
    public Guid CustomerId { get; set; }
    public DateTimeOffset ReadThrough { get; set; }
}

public sealed class SqliteCustomerNotificationStore(TransactionDbContext context) : ICustomerNotificationStore
{
    private const int SqliteConstraintError = 19;

    public async Task<CustomerNotificationFeed> ReadAsync(
        Guid customerId, int limit, CancellationToken cancellationToken)
    {
        var readThrough = await context.NotificationReadMarkers.AsNoTracking()
            .Where(item => item.CustomerId == customerId)
            .Select(item => (DateTimeOffset?)item.ReadThrough)
            .SingleOrDefaultAsync(cancellationToken) ?? DateTimeOffset.MinValue;

        var events = context.DisputeEvents.AsNoTracking().Join(
            context.Disputes.AsNoTracking().Where(dispute => dispute.CustomerId == customerId),
            entry => entry.DisputeId, dispute => dispute.Id,
            (entry, dispute) => new { Entry = entry, dispute.Reference });
        var unreadCount = await events.CountAsync(item => item.Entry.OccurredAt > readThrough, cancellationToken);
        var rows = await events
            .OrderByDescending(item => item.Entry.OccurredAt)
            .ThenByDescending(item => item.Entry.SequenceNumber)
            .ThenBy(item => item.Entry.Id)
            .Take(limit)
            .ToArrayAsync(cancellationToken);
        return new CustomerNotificationFeed(unreadCount, rows.Select(row => new CustomerNotification(
            row.Entry.Id, row.Entry.DisputeId, row.Reference, row.Entry.EventType,
            row.Entry.PublicDescription, row.Entry.OccurredAt, row.Entry.OccurredAt <= readThrough)).ToArray());
    }

    public async Task MarkReadAsync(Guid customerId, DateTimeOffset readThrough, CancellationToken cancellationToken)
    {
        if (!await AdvanceAsync(customerId, readThrough, cancellationToken))
            return;
        // First read for this customer: create the marker; a concurrent first write falls back to advancing it.
        context.NotificationReadMarkers.Add(new PersistedNotificationReadMarker
        {
            CustomerId = customerId, ReadThrough = readThrough
        });
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqliteException
            { SqliteErrorCode: SqliteConstraintError })
        {
            context.ChangeTracker.Clear();
            await AdvanceAsync(customerId, readThrough, cancellationToken);
        }
    }

    /// <summary>The marker only moves forward. Returns true when no marker exists yet.</summary>
    private async Task<bool> AdvanceAsync(Guid customerId, DateTimeOffset readThrough, CancellationToken cancellationToken)
    {
        if (!await context.NotificationReadMarkers.AnyAsync(item => item.CustomerId == customerId, cancellationToken))
            return true;
        await context.NotificationReadMarkers
            .Where(item => item.CustomerId == customerId && item.ReadThrough < readThrough)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ReadThrough, readThrough),
                cancellationToken);
        return false;
    }
}
