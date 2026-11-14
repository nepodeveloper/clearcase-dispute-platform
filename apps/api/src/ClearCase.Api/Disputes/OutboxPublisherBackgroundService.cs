using ClearCase.Application.Disputes;
using ClearCase.Infrastructure.Disputes;
using Confluent.Kafka;
using System.Text.Json;

namespace ClearCase.Api.Disputes;

internal sealed class OutboxPublisherBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<OutboxPublisherBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>();
                if (await dispatcher.DispatchNextAsync(stoppingToken))
                    continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Outbox dispatch cycle failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }
}

internal sealed class KafkaConsumerBackgroundService(
    IConfiguration configuration,
    IServiceScopeFactory scopeFactory,
    ILogger<KafkaConsumerBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var topic = configuration["Messaging:Kafka:Topic"] ?? "disputes.case-events.v1";
        var consumerName = configuration["Messaging:Kafka:ConsumerGroup"] ?? "clearcase-local-v1";
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = configuration["Messaging:Kafka:BootstrapServers"],
            GroupId = consumerName,
            EnableAutoCommit = false,
            EnableAutoOffsetStore = false,
            AutoOffsetReset = AutoOffsetReset.Earliest
        }).Build();
        consumer.Subscribe(topic);

        while (!stoppingToken.IsCancellationRequested)
        {
            ConsumeResult<string, string>? record;
            try
            {
                record = consumer.Consume(stoppingToken);
                var envelope = JsonSerializer.Deserialize<DisputeEventEnvelope>(record.Message.Value)
                    ?? throw new InvalidDataException("Kafka event envelope is empty.");
                if (envelope.EventVersion != 1 || envelope.EventId == Guid.Empty
                    || envelope.AggregateId == Guid.Empty || envelope.AggregateVersion < 1)
                    throw new InvalidDataException("Kafka event envelope version or identifiers are invalid.");

                await using var scope = scopeFactory.CreateAsyncScope();
                var inbox = scope.ServiceProvider.GetRequiredService<SqliteInboxProcessor>();
                await inbox.ProcessAsync(consumerName, envelope.ToOutboxEvent(), stoppingToken);
                consumer.Commit(record);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (ConsumeException exception)
            {
                logger.LogWarning(exception, "Kafka consumer transport failed for {Topic}.", topic);
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }
    }
}