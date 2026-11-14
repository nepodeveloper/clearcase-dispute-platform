using ClearCase.Application.Disputes;
using ClearCase.Infrastructure.Disputes;
using ClearCase.Infrastructure.Transactions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

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
