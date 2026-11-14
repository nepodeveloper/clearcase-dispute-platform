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

public interface IOutboxStore
{
    Task<OutboxEvent?> ReadNextAsync(CancellationToken cancellationToken);

    Task RecordAttemptAsync(Guid eventId, CancellationToken cancellationToken);

    Task MarkPublishedAsync(Guid eventId, DateTimeOffset publishedAt, CancellationToken cancellationToken);
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