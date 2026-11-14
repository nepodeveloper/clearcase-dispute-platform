using ClearCase.Application.Transactions;
using ClearCase.Infrastructure.Authentication;
using ClearCase.Domain.Transactions;
using ClearCase.Infrastructure.Disputes;
using ClearCase.Infrastructure.Chargebacks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ClearCase.Infrastructure.Transactions;

public sealed class TransactionDbContext(DbContextOptions<TransactionDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<PersistedTransaction> Transactions => Set<PersistedTransaction>();
    public DbSet<PersistedDispute> Disputes => Set<PersistedDispute>();
    public DbSet<PersistedDisputeEvent> DisputeEvents => Set<PersistedDisputeEvent>();
    public DbSet<PersistedDisputeSubmission> DisputeSubmissions => Set<PersistedDisputeSubmission>();
    public DbSet<PersistedOutboxMessage> OutboxMessages => Set<PersistedOutboxMessage>();
    public DbSet<PersistedChargeback> Chargebacks => Set<PersistedChargeback>();
    public DbSet<ChargebackAuditEvent> ChargebackAuditEvents => Set<ChargebackAuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var transaction = modelBuilder.Entity<PersistedTransaction>();
        transaction.ToTable("Transactions", table =>
            table.HasCheckConstraint("CK_Transactions_ZarCurrency", "\"Currency\" = 'ZAR'"));
        transaction.HasKey(item => item.Id);
        transaction.Property(item => item.Type).HasConversion<string>().IsRequired();
        transaction.Property(item => item.Status).HasConversion<string>().IsRequired();
        transaction.Property(item => item.MandateType).HasConversion<string>();
        transaction.Property(item => item.TransactionDate).HasConversion(
            value => value.UtcTicks,
            value => new DateTimeOffset(value, TimeSpan.Zero));
        transaction.Property(item => item.Description).IsRequired();
        transaction.Property(item => item.Currency).IsRequired();
        transaction.HasIndex(item => new { item.CustomerId, item.TransactionDate, item.Id });
        transaction.HasIndex(item => new { item.CustomerId, item.Type, item.TransactionDate, item.Id });

        DisputePersistenceModel.Configure(modelBuilder);
        OutboxPersistenceModel.Configure(modelBuilder);
        ChargebackPersistenceModel.Configure(modelBuilder);
    }
}

public sealed class PersistedTransaction
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public TransactionType Type { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? MerchantName { get; set; }
    public long AmountMinorUnits { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset TransactionDate { get; set; }
    public TransactionStatus Status { get; set; }
    public string? Reference { get; set; }
    public bool IsDisputable { get; set; }
    public bool IsDebit { get; set; }
    public bool HasRequiredDisputeInformation { get; set; }
    public DateOnly? CollectionDate { get; set; }
    public DebitOrderMandateType? MandateType { get; set; }
    public bool? MandateAuthorised { get; set; }
    public bool? MandateMatchesCollection { get; set; }
}
