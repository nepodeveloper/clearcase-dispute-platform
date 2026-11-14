using ClearCase.Application.Disputes;
using ClearCase.Infrastructure.Transactions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ClearCase.Infrastructure.Disputes;

internal static class KafkaInboxPersistenceModel
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        var inbox = modelBuilder.Entity<PersistedConsumerInboxMessage>();
        inbox.ToTable("ConsumerInbox");
        inbox.HasKey(item => new { item.ConsumerName, item.EventId });
        inbox.Property(item => item.ConsumerName).HasMaxLength(128).IsRequired();
        inbox.Property(item => item.EventType).IsRequired();
        inbox.Property(item => item.ProcessedAt).HasConversion(
            value => value.UtcTicks,
            value => new DateTimeOffset(value, TimeSpan.Zero));

        var effect = modelBuilder.Entity<PersistedConsumerEffect>();
        effect.ToTable("ConsumerEffects");
        effect.HasKey(item => new { item.ConsumerName, item.EventId });
        effect.Property(item => item.ConsumerName).HasMaxLength(128).IsRequired();
        effect.Property(item => item.EffectType).IsRequired();
        effect.Property(item => item.ProcessedAt).HasConversion(
            value => value.UtcTicks,
            value => new DateTimeOffset(value, TimeSpan.Zero));
    }
}

public sealed class PersistedConsumerInboxMessage
{
    public string ConsumerName { get; set; } = string.Empty;
    public Guid EventId { get; set; }
    public Guid AggregateId { get; set; }
    public long AggregateVersion { get; set; }
    public string EventType { get; set; } = string.Empty;
    public DateTimeOffset ProcessedAt { get; set; }
}

public sealed class PersistedConsumerEffect
{
    public string ConsumerName { get; set; } = string.Empty;
    public Guid EventId { get; set; }
    public Guid AggregateId { get; set; }
    public string EffectType { get; set; } = string.Empty;
    public DateTimeOffset ProcessedAt { get; set; }
}

public sealed class SqliteInboxProcessor(TransactionDbContext context, TimeProvider clock)
{
    private const int SqliteConstraintError = 19;

    public async Task<bool> ProcessAsync(
        string consumerName, OutboxEvent message, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerName);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        context.ConsumerInbox.Add(new PersistedConsumerInboxMessage
        {
            ConsumerName = consumerName,
            EventId = message.EventId,
            AggregateId = message.AggregateId,
            AggregateVersion = message.AggregateVersion,
            EventType = message.EventType,
            ProcessedAt = clock.GetUtcNow()
        });
        context.ConsumerEffects.Add(new PersistedConsumerEffect
        {
            ConsumerName = consumerName,
            EventId = message.EventId,
            AggregateId = message.AggregateId,
            EffectType = "event-accepted",
            ProcessedAt = clock.GetUtcNow()
        });
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsInboxConflict(exception))
        {
            context.ChangeTracker.Clear();
            return false;
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static bool IsInboxConflict(DbUpdateException exception) =>
        exception.InnerException is SqliteException { SqliteErrorCode: SqliteConstraintError } sqlite
        && (sqlite.Message.Contains("ConsumerInbox.ConsumerName, ConsumerInbox.EventId", StringComparison.Ordinal)
            || sqlite.Message.Contains("ConsumerEffects.ConsumerName, ConsumerEffects.EventId", StringComparison.Ordinal));
}

