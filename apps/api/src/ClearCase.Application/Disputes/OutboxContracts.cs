namespace ClearCase.Application.Disputes;

using System.Text.Json;

public sealed record OutboxEvent(
    Guid EventId,
    Guid AggregateId,
    long AggregateVersion,
    string EventType,
    int EventVersion,
    string CorrelationId,
    string Payload,
    DateTimeOffset OccurredAt,
    int Attempts);

public sealed record DisputeEventEnvelope(
    Guid EventId,
    string EventType,
    int EventVersion,
    Guid AggregateId,
    long AggregateVersion,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    JsonElement Payload)
{
    public OutboxEvent ToOutboxEvent() => new(EventId, AggregateId, AggregateVersion, EventType,
        EventVersion, CorrelationId, Payload.GetRawText(), OccurredAt, 0);
}

/// <summary>A fenced delivery lease. A restarted worker may reclaim only after its expiry.</summary>
public sealed record OutboxClaim(OutboxEvent Message, Guid LeaseId);

public interface IOutboxStore
{
    Task<OutboxClaim?> ClaimNextAsync(DateTimeOffset now, TimeSpan leaseDuration, CancellationToken cancellationToken);

    Task<bool> MarkPublishedAsync(Guid eventId, Guid leaseId, DateTimeOffset publishedAt,
        CancellationToken cancellationToken);

    Task<bool> ScheduleRetryAsync(Guid eventId, Guid leaseId, DateTimeOffset nextAttemptAt,
        CancellationToken cancellationToken);

    Task<bool> ParkAsync(Guid eventId, Guid leaseId, DateTimeOffset parkedAt,
        string reasonCode, CancellationToken cancellationToken);
}

/// <summary>Safe operational metadata; message payload, correlation ID and private notes are omitted.</summary>
public sealed record ParkedOutboxItem(
    Guid EventId, Guid AggregateId, long AggregateVersion, string EventType,
    int Attempts, DateTimeOffset ParkedAt, string ReasonCode);

public enum OutboxRequeueOutcome { Requeued, NotFound, NotParked }

/// <summary>Supervisor-only recovery port with durable employee audit and no raw event payloads.</summary>
public interface IOutboxRecoveryStore
{
    Task<IReadOnlyList<ParkedOutboxItem>> ListParkedAsync(int maxItems,
        CancellationToken cancellationToken);

    Task<OutboxRequeueOutcome> RequeueAsync(Guid eventId, Guid employeeId,
        string reason, DateTimeOffset occurredAt, CancellationToken cancellationToken);
}

public interface IOutboxPublisher
{
    Task PublishAsync(OutboxEvent message, CancellationToken cancellationToken);
}

public interface IOutboxDispatcher
{
    Task<bool> DispatchNextAsync(CancellationToken cancellationToken);
}

public sealed record OutboxMetadata(string CorrelationId);