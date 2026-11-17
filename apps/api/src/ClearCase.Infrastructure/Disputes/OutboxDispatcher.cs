using ClearCase.Application.Disputes;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace ClearCase.Infrastructure.Disputes;

public sealed class OutboxDispatcher(
    IOutboxStore store,
    IOutboxPublisher publisher,
    TimeProvider clock,
    ILogger<OutboxDispatcher> logger) : IOutboxDispatcher
{
    // All deliveries are at-least-once. A process crash after broker acknowledgement and
    // before MarkPublished will replay the same EventId after the lease expires.
    // Exceeding this limit parks the original immutable message without marking it published.
    internal const int MaximumDeliveryAttempts = 8;
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromMinutes(5);

    public async Task<bool> DispatchNextAsync(CancellationToken cancellationToken)
    {
        var lease = await store.ClaimNextAsync(clock.GetUtcNow(), LeaseDuration, cancellationToken);
        if (lease is null) return false;
        try
        {
            await publisher.PublishAsync(lease.Message, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Do not release a lease on shutdown: a restart reclaims it after expiry.
            throw;
        }
        catch (Exception exception)
        {
            if (lease.Message.Attempts >= MaximumDeliveryAttempts)
            {
                var parked = await store.ParkAsync(lease.Message.EventId, lease.LeaseId,
                    clock.GetUtcNow(), "delivery.attemptsExhausted", cancellationToken);
                if (parked) OutboxDispatchMetrics.Parked.Add(1);
                logger.LogError(exception,
                    "Outbox delivery limit reached for {EventId} ({EventType}); parked={Parked}; needs Supervisor review",
                    lease.Message.EventId, lease.Message.EventType, parked);
                return false;
            }

            var delay = RetryDelay(lease.Message.Attempts);
            if (await store.ScheduleRetryAsync(lease.Message.EventId, lease.LeaseId,
                    clock.GetUtcNow().Add(delay), cancellationToken))
                OutboxDispatchMetrics.Retried.Add(1);
            logger.LogWarning(exception,
                "Outbox publish failed for {EventId} ({EventType}); retry after {Delay}",
                lease.Message.EventId, lease.Message.EventType, delay);
            return false;
        }

        if (!await store.MarkPublishedAsync(
                lease.Message.EventId, lease.LeaseId, clock.GetUtcNow(), cancellationToken))
        {
            // Lease expired/stolen during a slow publish. A second delivery may occur;
            // never acknowledge an event claimed by another worker.
            logger.LogWarning("Outbox lease was lost for event {EventId}", lease.Message.EventId);
            return false;
        }
        return true;
    }

    internal static TimeSpan RetryDelay(int attempts) =>
        TimeSpan.FromSeconds(Math.Min(MaximumRetryDelay.TotalSeconds,
            Math.Pow(2, Math.Clamp(attempts, 1, 9))));
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

public static class OutboxDispatchMetrics
{
    private static readonly Meter Meter = new("ClearCase.Outbox", "1.0.0");
    public static readonly Counter<long> Parked = Meter.CreateCounter<long>("clearcase.outbox.parked");
    public static readonly Counter<long> Retried = Meter.CreateCounter<long>("clearcase.outbox.retried");
    public static readonly Counter<long> Requeued = Meter.CreateCounter<long>("clearcase.outbox.requeued");
}
