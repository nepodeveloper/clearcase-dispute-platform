using ClearCase.Application.Disputes;
using ClearCase.Infrastructure.Disputes;
using ClearCase.Infrastructure.Transactions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace ClearCase.Api.IntegrationTests;

public sealed class KafkaInboxTests
{
    [Fact]
    public void KafkaMessage_UsesAggregatePartitionKeyAndVersionedEnvelope()
    {
        var eventId = Guid.NewGuid();
        var aggregateId = Guid.NewGuid();
        var message = new OutboxEvent(eventId, aggregateId, 5, "dispute.transitioned.v1", 1,
            "corr-5", "{\"caseStatus\":\"UnderReview\"}",
            new DateTimeOffset(2026, 11, 14, 8, 0, 0, TimeSpan.Zero), 2);

        var kafkaMessage = KafkaOutboxPublisher.CreateMessage(message);
        var envelope = JsonSerializer.Deserialize<DisputeEventEnvelope>(kafkaMessage.Value)!;

        Assert.Equal(aggregateId.ToString("N"), kafkaMessage.Key);
        Assert.Equal(eventId, envelope.EventId);
        Assert.Equal(5, envelope.AggregateVersion);
        Assert.Equal(1, envelope.EventVersion);
        Assert.Equal("corr-5", envelope.CorrelationId);
        Assert.Equal("UnderReview", envelope.Payload.GetProperty("caseStatus").GetString());
    }

    [Fact]
    public async Task MalformedAndUnsupportedKafkaRecordsAreDurablyQuarantinedWithoutExposingRawPayload()
    {
        await using var database = new TemporaryDatabase();
        await using var db = database.CreateContext();
        await db.Database.MigrateAsync();
        var processor = new SqliteKafkaRecordProcessor(db,
            new SqliteInboxProcessor(db, TimeProvider.System), TimeProvider.System);
        const string badPayload = "{ sensitive-local-test-payload !! }";
        Assert.Equal(KafkaRecordDisposition.Quarantined,
            await processor.ProcessAsync("worker-a", "disputes.case-events.v1", 0, 10,
                badPayload, default));
        Assert.Equal(KafkaRecordDisposition.Quarantined,
            await processor.ProcessAsync("worker-a", "disputes.case-events.v1", 0, 10,
                badPayload, default));
        var quarantine = await db.KafkaQuarantinedRecords.AsNoTracking().SingleAsync();
        Assert.Equal("malformed.json", quarantine.FailureCode);
        Assert.Equal(64, quarantine.PayloadSha256.Length);
        Assert.Equal(System.Text.Encoding.UTF8.GetByteCount(badPayload), quarantine.PayloadByteLength);
        Assert.DoesNotContain(badPayload, quarantine.PayloadSha256);
        Assert.Empty(await db.ConsumerInbox.ToArrayAsync());

        var invalid = new DisputeEventEnvelope(Guid.NewGuid(), "dispute.submitted.v1", 400,
            Guid.NewGuid(), 1, DateTimeOffset.UtcNow, "test",
            JsonSerializer.SerializeToElement(new { caseStatus = "Submitted" }));
        Assert.Equal(KafkaRecordDisposition.Quarantined,
            await processor.ProcessAsync("worker-a", "disputes.case-events.v1", 0, 11,
                JsonSerializer.Serialize(invalid), default));
        Assert.Equal("invalid.envelope",
            (await db.KafkaQuarantinedRecords.AsNoTracking().SingleAsync(x => x.Offset == 11)).FailureCode);
        Assert.Equal(2, await db.KafkaQuarantinedRecords.CountAsync());
    }

    [Fact]
    public async Task CorrectedReplayAfterQuarantineIsProcessedOnceAcrossWorkerRestarts()
    {
        await using var database = new TemporaryDatabase();
        await using (var setup = database.CreateContext())
            await setup.Database.MigrateAsync();

        var evt = new OutboxEvent(Guid.NewGuid(), Guid.NewGuid(), 1, "dispute.submitted.v1",
            1, "correlation-safe", "{}", DateTimeOffset.UtcNow, 0);
        var valid = KafkaOutboxPublisher.CreateMessage(evt).Value;
        await using (var firstDb = database.CreateContext())
        {
            var processor = new SqliteKafkaRecordProcessor(firstDb,
                new SqliteInboxProcessor(firstDb, TimeProvider.System), TimeProvider.System);
            Assert.Equal(KafkaRecordDisposition.Quarantined,
                await processor.ProcessAsync("consumer", "case-events", 3, 41, "broken", default));
            Assert.Equal(KafkaRecordDisposition.Applied,
                await processor.ProcessAsync("consumer", "case-events", 3, 42, valid, default));
        }
        await using (var restarted = database.CreateContext())
        {
            var processor = new SqliteKafkaRecordProcessor(restarted,
                new SqliteInboxProcessor(restarted, TimeProvider.System), TimeProvider.System);
            Assert.Equal(KafkaRecordDisposition.Duplicate,
                await processor.ProcessAsync("consumer", "case-events", 3, 43, valid, default));
            Assert.Single(await restarted.KafkaQuarantinedRecords.ToArrayAsync());
            Assert.Single(await restarted.ConsumerInbox.ToArrayAsync());
            Assert.Single(await restarted.ConsumerEffects.ToArrayAsync());
        }
    }

    [Fact]
    public async Task QuarantineMigrationIsReversibleAndDoesNotAlterExistingInbox()
    {
        await using var database = new TemporaryDatabase();
        await using var db = database.CreateContext();
        var migrator = db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>();
        await migrator.MigrateAsync("20261117090000_AddOutboxDeliveryLeases");
        var id = Guid.NewGuid();
        db.ConsumerInbox.Add(new PersistedConsumerInboxMessage
        {
            ConsumerName = "before-upgrade",
            EventId = id,
            AggregateId = Guid.NewGuid(),
            AggregateVersion = 1,
            EventType = "dispute.submitted.v1",
            ProcessedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        await migrator.MigrateAsync();
        Assert.Equal(20, (await db.Database.GetAppliedMigrationsAsync()).Count());
        Assert.Single(await db.ConsumerInbox.AsNoTracking().ToArrayAsync());

        var processor = new SqliteKafkaRecordProcessor(db,
            new SqliteInboxProcessor(db, TimeProvider.System), TimeProvider.System);
        await processor.ProcessAsync("consumer", "case-events", 1, 5, "bad", default);
        Assert.Single(await db.KafkaQuarantinedRecords.ToArrayAsync());

        await migrator.MigrateAsync("20261117090000_AddOutboxDeliveryLeases");
        db.ChangeTracker.Clear();
        Assert.Single(await db.ConsumerInbox.AsNoTracking().ToArrayAsync());
        Assert.Equal(16, (await db.Database.GetAppliedMigrationsAsync()).Count());
    }

    [Fact]
    public async Task Process_DuplicateEventAppliesOneEffectPerConsumerAndReplaysAfterContextRestart()
    {
        await using var database = new TemporaryDatabase();
        await using (var setup = database.CreateContext())
            await setup.Database.MigrateAsync();
        var message = new OutboxEvent(Guid.NewGuid(), Guid.NewGuid(), 3, "dispute.transitioned.v1", 1,
            "correlation-123", "{}", DateTimeOffset.UtcNow, 0);

        await using (var firstContext = database.CreateContext())
        {
            var processor = new SqliteInboxProcessor(firstContext, TimeProvider.System);
            Assert.True(await processor.ProcessAsync("notification-consumer", message, default));
        }
        await using (var restartedContext = database.CreateContext())
        {
            var processor = new SqliteInboxProcessor(restartedContext, TimeProvider.System);
            Assert.False(await processor.ProcessAsync("notification-consumer", message, default));
            Assert.True(await processor.ProcessAsync("audit-consumer", message, default));
            Assert.Equal(2, await restartedContext.ConsumerInbox.CountAsync());
            Assert.Equal(2, await restartedContext.ConsumerEffects.CountAsync());
        }
    }
}
