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

/// <summary>One poisoned record must not stop a partition or the application host.</summary>
internal sealed class KafkaConsumerBackgroundService(
    IConfiguration configuration,
    IServiceScopeFactory scopeFactory,
    ILogger<KafkaConsumerBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var topic = configuration["Messaging:Kafka:Topic"] ?? "disputes.case-events.v1";
        var consumerName = configuration["Messaging:Kafka:ConsumerGroup"] ?? "clearcase-local-v1";
        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = configuration["Messaging:Kafka:BootstrapServers"],
            GroupId = consumerName,
            EnableAutoCommit = false,
            EnableAutoOffsetStore = false,
            AutoOffsetReset = AutoOffsetReset.Earliest
        };
        KafkaClientSecurity.Apply(consumerConfig, configuration);
        using var consumer = new ConsumerBuilder<string, string>(consumerConfig).Build();
        consumer.Subscribe(topic);

        while (!stoppingToken.IsCancellationRequested)
        {
            ConsumeResult<string, string>? record = null;
            try
            {
                record = consumer.Consume(stoppingToken);
                if (record is null) continue;
                await using var scope = scopeFactory.CreateAsyncScope();
                var handler = scope.ServiceProvider.GetRequiredService<IKafkaRecordProcessor>();
                var disposition = await handler.ProcessAsync(consumerName, record.Topic,
                    record.Partition.Value, record.Offset.Value, record.Message?.Value, stoppingToken);
                // A malformed record is committed only AFTER its quarantine metadata is durable.
                // Valid records are committed only AFTER the inbox + effect transaction commits.
                consumer.Commit(record);
                if (disposition == KafkaRecordDisposition.Quarantined)
                    logger.LogWarning(
                        "Quarantined Kafka record at {Topic}/{Partition}/{Offset}; raw payload not logged.",
                        record.Topic, record.Partition.Value, record.Offset.Value);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (ConsumeException exception)
            {
                KafkaConsumerMetrics.Failed.Add(1);
                logger.LogWarning(exception, "Kafka consumer transport failed for {Topic}.", topic);
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
            catch (Exception exception)
            {
                // Includes temporary database and offset-commit failures. Do NOT acknowledge
                // the record. Seek back to the failing offset; the durable inbox deduplicates
                // an already-applied effect on retry/restart.
                KafkaConsumerMetrics.Failed.Add(1);
                logger.LogError(exception,
                    "Kafka processing or commit failed for {Topic}/{Partition}/{Offset}.",
                    record?.Topic ?? topic, record?.Partition.Value, record?.Offset.Value);
                if (record is not null)
                {
                    try { consumer.Seek(record.TopicPartitionOffset); }
                    catch (KafkaException rebalance)
                    {
                        logger.LogWarning(rebalance, "Could not seek failed record; consumer will recover on reassignment.");
                    }
                }
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }
    }
}
