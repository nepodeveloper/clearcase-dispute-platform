using ClearCase.Infrastructure.Disputes;
using Microsoft.EntityFrameworkCore;

namespace ClearCase.Infrastructure.AgentDisputes;

internal static class AgentDisputePersistenceModel
{
    public const string IdempotencyIndex = "IX_InvestigationNotes_DisputeId_EmployeeId_IdempotencyKey";

    public static void Configure(ModelBuilder builder)
    {
        var note = builder.Entity<PersistedInvestigationNote>();
        note.ToTable("InvestigationNotes");
        note.HasKey(item => item.Id);
        note.Property(item => item.Text).IsRequired();
        note.Property(item => item.CorrelationId).IsRequired();
        note.Property(item => item.RequestFingerprint).HasMaxLength(64).IsRequired();
        note.Property(item => item.IdempotencyKey).HasMaxLength(128).IsRequired();
        note.Property(item => item.CreatedAt).HasConversion(
            value => value.UtcTicks,
            value => new DateTimeOffset(value, TimeSpan.Zero));
        note.HasIndex(item => new { item.DisputeId, item.EmployeeId, item.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName(IdempotencyIndex);
        note.HasIndex(item => new { item.DisputeId, item.CreatedAt, item.Id });
        note.HasOne<PersistedDispute>().WithMany()
            .HasForeignKey(item => item.DisputeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PersistedInvestigationNote
{
    public Guid Id { get; set; }
    public Guid DisputeId { get; set; }
    public Guid EmployeeId { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string RequestFingerprint { get; set; } = string.Empty;
}
