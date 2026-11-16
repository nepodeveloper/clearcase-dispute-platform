using ClearCase.Domain.Disputes;
using ClearCase.Infrastructure.Disputes;
using Microsoft.EntityFrameworkCore;

namespace ClearCase.Infrastructure.SupervisorEscalations;

internal static class SupervisorEscalationPersistenceModel
{
    public const string ActiveEscalationIndex = "IX_DisputeEscalations_DisputeId_Active";
    public const string CreateIdempotencyIndex = "IX_DisputeEscalations_DisputeId_EmployeeId_IdempotencyKey";
    public const string ActionIdempotencyIndex = "IX_DisputeEscalationAuditEvents_EscalationId_EmployeeId_IdempotencyKey";

    public static void Configure(ModelBuilder builder)
    {
        var escalation = builder.Entity<PersistedDisputeEscalation>();
        escalation.ToTable("DisputeEscalations");
        escalation.HasKey(item => item.Id);
        escalation.Property(item => item.Reference).HasMaxLength(64).IsRequired();
        escalation.Property(item => item.Reason).HasMaxLength(100).IsRequired();
        escalation.Property(item => item.Status).HasConversion<string>().IsRequired().IsConcurrencyToken();
        escalation.Property(item => item.IdempotencyKey).HasMaxLength(128).IsRequired();
        escalation.Property(item => item.RequestFingerprint).HasMaxLength(64).IsRequired();
        escalation.Property(item => item.CreatedAt).HasConversion(
            value => value.UtcTicks,
            value => new DateTimeOffset(value, TimeSpan.Zero));
        escalation.HasIndex(item => new { item.DisputeId, item.Status })
            .IsUnique()
            .HasFilter($"\"Status\" <> '{DisputeEscalationStatus.Resolved}'")
            .HasDatabaseName(ActiveEscalationIndex);
        escalation.HasIndex(item => new { item.DisputeId, item.CreatedByEmployeeId, item.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName(CreateIdempotencyIndex);
        escalation.HasIndex(item => new { item.CreatedAt, item.Reference }).IsUnique();
        escalation.HasOne<PersistedDispute>().WithMany()
            .HasForeignKey(item => item.DisputeId)
            .OnDelete(DeleteBehavior.Restrict);

        var audit = builder.Entity<PersistedDisputeEscalationAuditEvent>();
        audit.ToTable("DisputeEscalationAuditEvents");
        audit.HasKey(item => item.Id);
        audit.Property(item => item.Action).HasConversion<string>().IsRequired();
        audit.Property(item => item.PrivateNote).HasMaxLength(2000);
        audit.Property(item => item.SequenceNumber).IsRequired();
        audit.Property(item => item.CorrelationId).IsRequired();
        audit.Property(item => item.IdempotencyKey).HasMaxLength(128).IsRequired();
        audit.Property(item => item.RequestFingerprint).HasMaxLength(64).IsRequired();
        audit.Property(item => item.OccurredAt).HasConversion(
            value => value.UtcTicks,
            value => new DateTimeOffset(value, TimeSpan.Zero));
        audit.HasIndex(item => new { item.EscalationId, item.SequenceNumber }).IsUnique();
        audit.HasIndex(item => new { item.EscalationId, item.EmployeeId, item.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName(ActionIdempotencyIndex);
        audit.HasOne<PersistedDisputeEscalation>().WithMany(item => item.History)
            .HasForeignKey(item => item.EscalationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PersistedDisputeEscalation
{
    public Guid Id { get; set; }
    public string Reference { get; set; } = string.Empty;
    public Guid DisputeId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DisputeEscalationStatus Status { get; set; }
    public Guid CreatedByEmployeeId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string RequestFingerprint { get; set; } = string.Empty;
    public List<PersistedDisputeEscalationAuditEvent> History { get; set; } = [];
}

public sealed class PersistedDisputeEscalationAuditEvent
{
    public Guid Id { get; set; }
    public Guid EscalationId { get; set; }
    public int SequenceNumber { get; set; }
    public DisputeEscalationAction Action { get; set; }
    public Guid EmployeeId { get; set; }
    public string? PrivateNote { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string RequestFingerprint { get; set; } = string.Empty;
}
