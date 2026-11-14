using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace ClearCase.Infrastructure.Disputes;

internal static class OutboxPersistenceModel
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        var message = modelBuilder.Entity<PersistedOutboxMessage>();
        message.ToTable("OutboxMessages");
        message.HasKey(item => item.EventId);
        message.Property(item => item.EventType).IsRequired();
        message.Property(item => item.CorrelationId).IsRequired();
        message.Property(item => item.Payload).IsRequired();
        message.Property(item => item.OccurredAt).HasConversion(
            value => value.UtcTicks,
            value => new DateTimeOffset(value, TimeSpan.Zero));
        message.Property(item => item.PublishedAt).HasConversion(
            value => value.HasValue ? value.Value.UtcTicks : (long?)null,
            value => value.HasValue ? new DateTimeOffset(value.Value, TimeSpan.Zero) : (DateTimeOffset?)null);
        message.HasIndex(item => new { item.PublishedAt, item.OccurredAt });
        message.HasIndex(item => new { item.AggregateId, item.AggregateVersion }).IsUnique();
        message.HasOne<PersistedDispute>().WithMany()
            .HasForeignKey(item => item.AggregateId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PersistedOutboxMessage
{
    public Guid EventId { get; set; }
    public Guid AggregateId { get; set; }
    public long AggregateVersion { get; set; }
    public string EventType { get; set; } = string.Empty;
    public int EventVersion { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public int Attempts { get; set; }

    public static PersistedOutboxMessage Submitted(PersistedDispute dispute, string correlationId) => Create(
        dispute, "dispute.submitted.v1", dispute.CreatedAt, correlationId, new
        {
            disputeId = dispute.Id,
            caseStatus = dispute.CaseStatus.ToString(),
            decisionStatus = dispute.DecisionStatus.ToString(),
            financialStatus = dispute.FinancialStatus.ToString()
        });

    public static PersistedOutboxMessage Transitioned(
        PersistedDispute dispute, string command, DateTimeOffset occurredAt, string eventType,
        string correlationId) => Create(
        dispute, eventType, occurredAt, correlationId, new
        {
            disputeId = dispute.Id,
            command,
            caseStatus = dispute.CaseStatus.ToString(),
            decisionStatus = dispute.DecisionStatus.ToString(),
            financialStatus = dispute.FinancialStatus.ToString()
        });

    private static PersistedOutboxMessage Create(
        PersistedDispute dispute, string eventType, DateTimeOffset occurredAt, string correlationId, object payload) => new()
    {
        EventId = Guid.NewGuid(),
        AggregateId = dispute.Id,
        AggregateVersion = dispute.Version,
        EventType = eventType,
        EventVersion = 1,
        CorrelationId = string.IsNullOrWhiteSpace(correlationId) ? "local" : correlationId,
        Payload = JsonSerializer.Serialize(payload),
        OccurredAt = occurredAt
    };
}