using ClearCase.Domain.Chargebacks;
using ClearCase.Domain.Disputes;
using ClearCase.Infrastructure.Disputes;
using Microsoft.EntityFrameworkCore;

namespace ClearCase.Infrastructure.Chargebacks;

internal static class ChargebackPersistenceModel
{
    public static void Configure(ModelBuilder builder)
    {
        var chargeback = builder.Entity<PersistedChargeback>();
        chargeback.ToTable("Chargebacks", table => table.HasCheckConstraint("CK_Chargebacks_EvidenceMetadata",
            """("EvidenceId" IS NULL AND "EvidenceKind" IS NULL AND "EvidenceReceivedAt" IS NULL) OR ("EvidenceId" IS NOT NULL AND "EvidenceKind" IS NOT NULL AND "EvidenceReceivedAt" IS NOT NULL)"""));
        chargeback.HasKey(item => item.DisputeId);
        chargeback.Property(item => item.Reference).IsRequired();
        chargeback.HasIndex(item => item.Reference).IsUnique();
        chargeback.Property(item => item.ReasonCode).IsRequired();
        chargeback.Property(item => item.Status).HasConversion<string>();
        chargeback.Property(item => item.LastOperationOutcome).HasConversion<string>();
        chargeback.Property(item => item.EvidenceKind).HasConversion<string>();
        chargeback.Property(item => item.EvidenceReceivedAt).HasConversion(
            value => value.HasValue ? value.Value.UtcTicks : (long?)null,
            value => value.HasValue ? new DateTimeOffset(value.Value, TimeSpan.Zero) : null);
        chargeback.HasOne<PersistedDispute>().WithOne(item => item.Chargeback)
            .HasForeignKey<PersistedChargeback>(item => item.DisputeId).OnDelete(DeleteBehavior.Restrict);

        var audit = builder.Entity<ChargebackAuditEvent>();
        audit.ToTable("ChargebackAuditEvents");
        audit.HasKey(item => item.Id);
        audit.Property(item => item.OperationReference).IsRequired();
        audit.HasIndex(item => item.OperationReference).IsUnique();
        audit.Property(item => item.Command).HasConversion<string>();
        audit.Property(item => item.ChargebackStatus).HasConversion<string>();
        audit.Property(item => item.DecisionStatus).HasConversion<string>();
        audit.Property(item => item.FinancialStatus).HasConversion<string>();
        audit.Property(item => item.Outcome).HasConversion<string>();
        audit.Property(item => item.EvidenceKind).HasConversion<string>();
        audit.Property(item => item.CorrelationId).IsRequired();
        audit.Property(item => item.OccurredAt).HasConversion(
            value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        audit.HasOne<PersistedChargeback>().WithMany()
            .HasForeignKey(item => item.DisputeId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PersistedChargeback
{
    public Guid DisputeId { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string ReasonCode { get; set; } = string.Empty;
    public ChargebackStatus Status { get; set; }
    public CardNetworkOutcome LastOperationOutcome { get; set; }
    public Guid? EvidenceId { get; set; }
    public ChargebackEvidenceKind? EvidenceKind { get; set; }
    public DateTimeOffset? EvidenceReceivedAt { get; set; }

    public Chargeback ToDomain() => new(DisputeId, Reference, ReasonCode, Status, LastOperationOutcome,
        EvidenceId is { } id ? new RepresentmentEvidence(id,
            EvidenceKind ?? throw new InvalidOperationException("Stored evidence kind is missing."),
            EvidenceReceivedAt ?? throw new InvalidOperationException("Stored evidence timestamp is missing.")) : null);

    public void Update(Chargeback chargeback)
    {
        DisputeId = chargeback.DisputeId;
        Reference = chargeback.Reference;
        ReasonCode = chargeback.ReasonCode;
        Status = chargeback.Status;
        LastOperationOutcome = chargeback.LastOperationOutcome;
        EvidenceId = chargeback.Evidence?.Id;
        EvidenceKind = chargeback.Evidence?.Kind;
        EvidenceReceivedAt = chargeback.Evidence?.ReceivedAt;
    }
}

public sealed class ChargebackAuditEvent
{
    public Guid Id { get; set; }
    public Guid DisputeId { get; set; }
    public string OperationReference { get; set; } = string.Empty;
    public ChargebackCommand Command { get; set; }
    public ChargebackStatus ChargebackStatus { get; set; }
    public DisputeDecisionStatus DecisionStatus { get; set; }
    public DisputeFinancialStatus FinancialStatus { get; set; }
    public CardNetworkOutcome Outcome { get; set; }
    public Guid ActorId { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public Guid? EvidenceId { get; set; }
    public ChargebackEvidenceKind? EvidenceKind { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
