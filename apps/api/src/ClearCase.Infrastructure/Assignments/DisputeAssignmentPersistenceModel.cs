using ClearCase.Domain.Disputes;
using ClearCase.Infrastructure.Disputes;
using Microsoft.EntityFrameworkCore;

namespace ClearCase.Infrastructure.Assignments;

internal static class DisputeAssignmentPersistenceModel
{
    public static void Configure(ModelBuilder builder)
    {
        var assignment = builder.Entity<PersistedDisputeAssignment>();
        assignment.ToTable("DisputeAssignments");
        assignment.HasKey(item => item.DisputeId);
        assignment.Property(item => item.AssignedAt).HasConversion(
            value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        assignment.HasIndex(item => item.EmployeeId);
        assignment.HasOne<PersistedDispute>().WithMany()
            .HasForeignKey(item => item.DisputeId).OnDelete(DeleteBehavior.Restrict);

        var audit = builder.Entity<PersistedDisputeAssignmentAuditEvent>();
        audit.ToTable("DisputeAssignmentAuditEvents");
        audit.HasKey(item => item.Id);
        audit.Property(item => item.Action).HasConversion<string>().IsRequired();
        audit.Property(item => item.CorrelationId).IsRequired();
        audit.Property(item => item.OccurredAt).HasConversion(
            value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        audit.HasIndex(item => new { item.DisputeId, item.OccurredAt });
        audit.HasOne<PersistedDispute>().WithMany()
            .HasForeignKey(item => item.DisputeId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>At most one current assignee per dispute; the key enforces it under concurrent claims.</summary>
public sealed class PersistedDisputeAssignment
{
    public Guid DisputeId { get; set; }
    public Guid EmployeeId { get; set; }
    public DateTimeOffset AssignedAt { get; set; }
}

/// <summary>Append-only history; database triggers reject UPDATE and DELETE. EmployeeId is the acting employee.</summary>
public sealed class PersistedDisputeAssignmentAuditEvent
{
    public Guid Id { get; set; }
    public Guid DisputeId { get; set; }
    public Guid EmployeeId { get; set; }
    public DisputeAssignmentAction Action { get; set; }
    public Guid? PreviousEmployeeId { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }
}
