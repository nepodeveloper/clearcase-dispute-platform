using ClearCase.Application.Disputes;
using Microsoft.EntityFrameworkCore;

namespace ClearCase.Infrastructure.Disputes;

public enum FinancialOperationStage { Pending, Dispatched, ResultRecorded, Completed }

public sealed class PersistedFinancialOperation
{
    public Guid Id { get; set; }
    public Guid DisputeId { get; set; }
    public Guid CustomerId { get; set; }
    public FinancialOperationKind Kind { get; set; }
    public string OperationReference { get; set; } = string.Empty;
    public long ExpectedVersion { get; set; }
    public Guid? EmployeeId { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public FinancialOperationStage Stage { get; set; }
    public string? Outcome { get; set; }
    public int Attempts { get; set; }
    public Guid? LeaseId { get; set; }
    public DateTimeOffset? LeaseUntilAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

internal static class FinancialOperationPersistenceModel
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PersistedFinancialOperation>();
        entity.ToTable("FinancialOperationIntents");
        entity.HasKey(item => item.Id);
        entity.Property(item => item.Kind).HasConversion<string>().IsRequired();
        entity.Property(item => item.Stage).HasConversion<string>().IsRequired();
        entity.Property(item => item.OperationReference).HasMaxLength(128).IsRequired();
        entity.Property(item => item.CorrelationId).HasMaxLength(128).IsRequired();
        entity.Property(item => item.CreatedAt).HasConversion(
            value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        entity.Property(item => item.UpdatedAt).HasConversion(
            value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        entity.Property(item => item.LeaseUntilAt).HasConversion(
            value => value.HasValue ? value.Value.UtcTicks : (long?)null,
            value => value.HasValue ? new DateTimeOffset(value.Value, TimeSpan.Zero) : (DateTimeOffset?)null);
        entity.HasIndex(item => item.OperationReference).IsUnique();
        entity.HasIndex(item => new { item.Stage, item.LeaseUntilAt, item.CreatedAt });
        entity.HasOne<PersistedDispute>().WithMany().HasForeignKey(item => item.DisputeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
