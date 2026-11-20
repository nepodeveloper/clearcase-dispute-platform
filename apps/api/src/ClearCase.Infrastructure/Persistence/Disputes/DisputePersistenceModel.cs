using ClearCase.Domain.DisputeEligibility;
using ClearCase.Domain.Disputes;
using ClearCase.Domain.Transactions;
using ClearCase.Infrastructure.Transactions;
using ClearCase.Infrastructure.Chargebacks;
using Microsoft.EntityFrameworkCore;

namespace ClearCase.Infrastructure.Disputes;

internal static class DisputePersistenceModel
{
    public const string OpenDisputeIndex = "IX_Disputes_CustomerId_TransactionId_Open";
    public const int IdempotencyKeyMaxLength = 128;

    public static void Configure(ModelBuilder modelBuilder)
    {
        var dispute = modelBuilder.Entity<PersistedDispute>();
        dispute.ToTable("Disputes");
        dispute.HasKey(item => item.Id);
        dispute.Property(item => item.Reference).IsRequired();
        dispute.Property(item => item.Category).HasConversion<string>().IsRequired();
        dispute.Property(item => item.ReasonCode).IsRequired();
        dispute.Property(item => item.Description).IsRequired();
        dispute.Property(item => item.CaseStatus).HasConversion<string>().IsRequired();
        dispute.Property(item => item.DecisionStatus).HasConversion<string>().IsRequired();
        dispute.Property(item => item.FinancialStatus).HasConversion<string>().IsRequired();
        dispute.Property(item => item.EligibilityOutcome).HasConversion<string>().IsRequired();
        dispute.Property(item => item.EligibilityPolicyVersion).IsRequired();
        dispute.Property(item => item.Version).HasDefaultValue(1L).IsConcurrencyToken();
        dispute.Property(item => item.CreatedAt).HasConversion(
            value => value.UtcTicks,
            value => new DateTimeOffset(value, TimeSpan.Zero));
        dispute.HasIndex(item => item.Reference).IsUnique();
        dispute.HasIndex(item => item.ReversalOperationReference).IsUnique();
        dispute.HasIndex(item => item.BankChargeAdjustmentReference).IsUnique();
        dispute.HasIndex(item => new { item.CustomerId, item.CreatedAt, item.Reference });
        // Database-enforced rule: at most one open case per customer transaction.
        dispute.HasIndex(item => new { item.CustomerId, item.TransactionId })
            .IsUnique()
            .HasFilter($"\"CaseStatus\" <> '{DisputeCaseStatus.Closed}'")
            .HasDatabaseName(OpenDisputeIndex);
        dispute.HasOne<PersistedTransaction>().WithMany()
            .HasForeignKey(item => item.TransactionId)
            .OnDelete(DeleteBehavior.Restrict);
        dispute.HasMany(item => item.Events).WithOne()
            .HasForeignKey(item => item.DisputeId)
            .OnDelete(DeleteBehavior.Restrict);

        var disputeEvent = modelBuilder.Entity<PersistedDisputeEvent>();
        disputeEvent.ToTable("DisputeEvents");
        disputeEvent.HasKey(item => item.Id);
        disputeEvent.Property(item => item.EventType).IsRequired();
        disputeEvent.Property(item => item.CaseStatus).HasConversion<string>().IsRequired();
        disputeEvent.Property(item => item.PublicDescription).IsRequired();
        disputeEvent.Property(item => item.OccurredAt).HasConversion(
            value => value.UtcTicks,
            value => new DateTimeOffset(value, TimeSpan.Zero));
        disputeEvent.HasIndex(item => new { item.DisputeId, item.SequenceNumber }).IsUnique();

        var submission = modelBuilder.Entity<PersistedDisputeSubmission>();
        submission.ToTable("DisputeSubmissions");
        submission.HasKey(item => new { item.CustomerId, item.IdempotencyKey });
        submission.Property(item => item.IdempotencyKey).HasMaxLength(IdempotencyKeyMaxLength);
        submission.Property(item => item.RequestFingerprint).IsRequired();
        submission.Property(item => item.CreatedAt).HasConversion(
            value => value.UtcTicks,
            value => new DateTimeOffset(value, TimeSpan.Zero));
        submission.HasOne<PersistedDispute>().WithMany()
            .HasForeignKey(item => item.DisputeId)
            .OnDelete(DeleteBehavior.Restrict);

        var audit = modelBuilder.Entity<PersistedDisputeCommandAuditEvent>();
        audit.ToTable("DisputeCommandAuditEvents");
        audit.HasKey(item => item.Id);
        audit.HasIndex(item => new { item.DisputeId, item.Version }).IsUnique();
        audit.Property(item => item.Command).IsRequired();
        audit.Property(item => item.CorrelationId).IsRequired();
        audit.Property(item => item.CaseStatus).HasConversion<string>();
        audit.Property(item => item.DecisionStatus).HasConversion<string>();
        audit.Property(item => item.FinancialStatus).HasConversion<string>();
        audit.Property(item => item.OccurredAt).HasConversion(
            value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        audit.HasOne<PersistedDispute>().WithMany()
            .HasForeignKey(item => item.DisputeId).OnDelete(DeleteBehavior.Restrict);

        var withdrawal = modelBuilder.Entity<PersistedDisputeWithdrawal>();
        withdrawal.ToTable("DisputeWithdrawals");
        withdrawal.HasKey(item => item.DisputeId);
        withdrawal.Property(item => item.Reason).HasMaxLength(DisputeWithdrawalRules.ReasonMaxLength);
        withdrawal.Property(item => item.WithdrawnAt).HasConversion(
            value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        withdrawal.HasOne<PersistedDispute>().WithMany()
            .HasForeignKey(item => item.DisputeId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>The customer's optional reason for walking away; the case itself is closed through the transition writer.</summary>
public sealed class PersistedDisputeWithdrawal
{
    public Guid DisputeId { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset WithdrawnAt { get; set; }
}

public sealed class PersistedDispute
{
    public Guid Id { get; set; }
    public string Reference { get; set; } = string.Empty;
    public Guid CustomerId { get; set; }
    public Guid TransactionId { get; set; }
    public TransactionCategory Category { get; set; }
    public string ReasonCode { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DisputeCaseStatus CaseStatus { get; set; }
    public DisputeDecisionStatus DecisionStatus { get; set; }
    public DisputeFinancialStatus FinancialStatus { get; set; }
    public DisputeEligibilityOutcome EligibilityOutcome { get; set; }
    public string EligibilityPolicyVersion { get; set; } = string.Empty;
    public long Version { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; }
    public string? ReversalOperationReference { get; set; }
    public string? BankChargeAdjustmentReference { get; set; }
    public PersistedChargeback? Chargeback { get; set; }
    public List<PersistedDisputeEvent> Events { get; set; } = [];
}

/// <summary>Append-only timeline row; database triggers reject UPDATE and DELETE.</summary>
public sealed class PersistedDisputeEvent
{
    public Guid Id { get; set; }
    public Guid DisputeId { get; set; }
    public int SequenceNumber { get; set; }
    public string EventType { get; set; } = string.Empty;
    public DisputeCaseStatus CaseStatus { get; set; }
    public string PublicDescription { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }
}

public sealed class PersistedDisputeSubmission
{
    public Guid CustomerId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string RequestFingerprint { get; set; } = string.Empty;
    public Guid DisputeId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class PersistedDisputeCommandAuditEvent
{
    public Guid Id { get; set; }
    public Guid DisputeId { get; set; }
    public long Version { get; set; }
    public Guid EmployeeId { get; set; }
    public string Command { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }
    public DisputeCaseStatus CaseStatus { get; set; }
    public DisputeDecisionStatus DecisionStatus { get; set; }
    public DisputeFinancialStatus FinancialStatus { get; set; }

    public static PersistedDisputeCommandAuditEvent Create(
        PersistedDispute dispute, Guid employeeId, string command, string correlationId, DateTimeOffset occurredAt) =>
        new()
        {
            Id = Guid.NewGuid(), DisputeId = dispute.Id, Version = dispute.Version,
            EmployeeId = employeeId, Command = command, CorrelationId = correlationId, OccurredAt = occurredAt,
            CaseStatus = dispute.CaseStatus, DecisionStatus = dispute.DecisionStatus, FinancialStatus = dispute.FinancialStatus
        };
}
