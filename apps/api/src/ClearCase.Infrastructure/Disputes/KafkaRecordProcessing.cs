using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClearCase.Application.Disputes;
using ClearCase.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;

namespace ClearCase.Infrastructure.Disputes;

public enum KafkaRecordDisposition { Applied, Duplicate, Quarantined }

/// <summary>
/// Quarantine metadata intentionally excludes raw payloads and exception text.
/// Operators can locate the original source record using topic/partition/offset.
/// </summary>
public sealed class PersistedKafkaQuarantinedRecord
{
    public string ConsumerName { get; set; } = string.Empty;
    public string Topic { get; set; } = string.Empty;
    public int Partition { get; set; }
    public long Offset { get; set; }
    public string FailureCode { get; set; } = string.Empty;
    public string PayloadSha256 { get; set; } = string.Empty;
    public int PayloadByteLength { get; set; }
    public DateTimeOffset QuarantinedAt { get; set; }
}

internal static class KafkaQuarantinePersistenceModel
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        var record = modelBuilder.Entity<PersistedKafkaQuarantinedRecord>();
        record.ToTable("KafkaQuarantinedRecords");
        record.HasKey(item => new { item.ConsumerName, item.Topic, item.Partition, item.Offset });
        record.Property(item => item.ConsumerName).HasMaxLength(128).IsRequired();
        record.Property(item => item.Topic).HasMaxLength(256).IsRequired();
        record.Property(item => item.FailureCode).HasMaxLength(64).IsRequired();
        record.Property(item => item.PayloadSha256).HasMaxLength(64).IsRequired();
        record.Property(item => item.QuarantinedAt).HasConversion(
            value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        record.HasIndex(item => item.QuarantinedAt);
    }
}

public static class KafkaConsumerMetrics
{
    private static readonly Meter Meter = new("ClearCase.Kafka", "1.0.0");
    public static readonly Counter<long> Processed = Meter.CreateCounter<long>("clearcase.kafka.processed");
    public static readonly Counter<long> Duplicate = Meter.CreateCounter<long>("clearcase.kafka.duplicate");
    public static readonly Counter<long> Quarantined = Meter.CreateCounter<long>("clearcase.kafka.quarantined");
    public static readonly Counter<long> Failed = Meter.CreateCounter<long>("clearcase.kafka.failed");
}

public sealed class SqliteKafkaRecordProcessor(
    TransactionDbContext database,
    SqliteInboxProcessor inbox,
    TimeProvider clock)
{
    public async Task<KafkaRecordDisposition> ProcessAsync(
        string consumerName, string topic, int partition, long offset, string? value,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        if (partition < 0 || offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        DisputeEventEnvelope envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<DisputeEventEnvelope>(value ?? string.Empty)
                ?? throw new InvalidDataException("Empty envelope");
            if (envelope.EventVersion != 1 || envelope.EventId == Guid.Empty
                || envelope.AggregateId == Guid.Empty || envelope.AggregateVersion < 1
                || string.IsNullOrWhiteSpace(envelope.EventType)
                || string.IsNullOrWhiteSpace(envelope.CorrelationId)
                || envelope.OccurredAt == default
                || envelope.Payload.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Invalid envelope fields");
        }
        catch (JsonException)
        {
            return await QuarantineAsync(consumerName, topic, partition, offset, value,
                "malformed.json", cancellationToken);
        }
        catch (InvalidDataException)
        {
            return await QuarantineAsync(consumerName, topic, partition, offset, value,
                "invalid.envelope", cancellationToken);
        }

        try
        {
            var applied = await inbox.ProcessAsync(consumerName, envelope.ToOutboxEvent(), cancellationToken);
            if (applied) KafkaConsumerMetrics.Processed.Add(1);
            else KafkaConsumerMetrics.Duplicate.Add(1);
            return applied ? KafkaRecordDisposition.Applied : KafkaRecordDisposition.Duplicate;
        }
        catch
        {
            KafkaConsumerMetrics.Failed.Add(1);
            throw; // No Kafka offset commit on transient DB failures.
        }
    }

    private async Task<KafkaRecordDisposition> QuarantineAsync(
        string consumerName, string topic, int partition, long offset, string? raw,
        string failureCode, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(raw ?? string.Empty);
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        if (!await database.Set<PersistedKafkaQuarantinedRecord>().AsNoTracking().AnyAsync(
                item => item.ConsumerName == consumerName && item.Topic == topic
                    && item.Partition == partition && item.Offset == offset, cancellationToken))
        {
            database.Set<PersistedKafkaQuarantinedRecord>().Add(new PersistedKafkaQuarantinedRecord
            {
                ConsumerName = consumerName,
                Topic = topic,
                Partition = partition,
                Offset = offset,
                FailureCode = failureCode,
                PayloadSha256 = Convert.ToHexString(SHA256.HashData(bytes)),
                PayloadByteLength = bytes.Length,
                QuarantinedAt = clock.GetUtcNow()
            });
            await database.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        KafkaConsumerMetrics.Quarantined.Add(1);
        return KafkaRecordDisposition.Quarantined;
    }
}
