using System.Text.Json;
using ClearCase.Application.Disputes;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ClearCase.Infrastructure.Disputes;

public sealed class KafkaOutboxPublisher(IProducer<string, string> producer, string topic) : IOutboxPublisher
{
    public async Task PublishAsync(OutboxEvent message, CancellationToken cancellationToken)
    {
        var kafkaMessage = CreateMessage(message);
        var result = await producer.ProduceAsync(topic, kafkaMessage, cancellationToken);
        if (result.Status != PersistenceStatus.Persisted)
            throw new InvalidOperationException("Kafka did not acknowledge the outbox event.");
    }

    public static Message<string, string> CreateMessage(OutboxEvent message)
    {
        using var payload = JsonDocument.Parse(message.Payload);
        var envelope = new DisputeEventEnvelope(message.EventId, message.EventType, message.EventVersion,
            message.AggregateId, message.AggregateVersion, message.OccurredAt, message.CorrelationId,
            payload.RootElement.Clone());
        return new Message<string, string>
        {
            Key = message.AggregateId.ToString("N"),
            Value = JsonSerializer.Serialize(envelope)
        };
    }
}

public static class KafkaOutboxRegistration
{
    public static IServiceCollection AddKafkaOutbox(this IServiceCollection services, IConfiguration configuration)
    {
        var bootstrapServers = configuration["Messaging:Kafka:BootstrapServers"];
        if (string.IsNullOrWhiteSpace(bootstrapServers))
            throw new InvalidOperationException("Messaging:Kafka:BootstrapServers is required when Kafka is enabled.");
        var topic = configuration["Messaging:Kafka:Topic"] ?? "disputes.case-events.v1";
        services.AddSingleton<IProducer<string, string>>(_ => new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = bootstrapServers,
            EnableIdempotence = true,
            Acks = Acks.All,
            MessageSendMaxRetries = 5
        }).Build());
        services.AddSingleton<IOutboxPublisher>(provider => new KafkaOutboxPublisher(
            provider.GetRequiredService<IProducer<string, string>>(), topic));
        services.AddScoped<SqliteInboxProcessor>();
        return services;
    }
}