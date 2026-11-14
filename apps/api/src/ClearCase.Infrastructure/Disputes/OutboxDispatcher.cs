using ClearCase.Application.Disputes;
using Microsoft.Extensions.Logging;

namespace ClearCase.Infrastructure.Disputes;

public sealed class OutboxDispatcher(
    IOutboxStore store,
    IOutboxPublisher publisher,
    TimeProvider clock,
    ILogger<OutboxDispatcher> logger) : IOutboxDispatcher
{
    public async Task<bool> DispatchNextAsync(CancellationToken cancellationToken)
    {
        var message = await store.ReadNextAsync(cancellationToken);
        if (message is null)
            return false;

        await store.RecordAttemptAsync(message.EventId, cancellationToken);
        try
        {
            await publisher.PublishAsync(message, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Outbox publish failed for {EventId} ({EventType})",
                message.EventId, message.EventType);
            return false;
        }

        await store.MarkPublishedAsync(message.EventId, clock.GetUtcNow(), cancellationToken);
        return true;
    }
}

public sealed class LocalOutboxPublisher(ILogger<LocalOutboxPublisher> logger) : IOutboxPublisher
{
    public Task PublishAsync(OutboxEvent message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        logger.LogInformation("Dispatched outbox event {EventId} {EventType} for aggregate {AggregateId} version {Version}",
            message.EventId, message.EventType, message.AggregateId, message.AggregateVersion);
        return Task.CompletedTask;
    }
}