using ClearCase.Application.Evidence;
using ClearCase.Domain.Disputes;
using ClearCase.Domain.Evidence;
using ClearCase.Infrastructure.Disputes;
using ClearCase.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;

namespace ClearCase.Infrastructure.Evidence;

internal static class CustomerEvidencePersistenceModel
{
    public static void Configure(ModelBuilder builder)
    {
        var evidence = builder.Entity<PersistedCustomerEvidence>();
        evidence.ToTable("CustomerEvidence");
        evidence.HasKey(item => item.Id);
        evidence.Property(item => item.FileName).HasMaxLength(CustomerEvidenceRules.FileNameMaxLength).IsRequired();
        evidence.Property(item => item.ContentType).HasMaxLength(64).IsRequired();
        evidence.Property(item => item.Sha256).HasMaxLength(64).IsRequired();
        evidence.Property(item => item.Content).IsRequired();
        evidence.Property(item => item.Note).HasMaxLength(CustomerEvidenceRules.NoteMaxLength);
        evidence.Property(item => item.UploadedAt).HasConversion(
            value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        evidence.HasIndex(item => new { item.DisputeId, item.UploadedAt, item.Id });
        evidence.HasOne<PersistedDispute>().WithMany()
            .HasForeignKey(item => item.DisputeId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PersistedCustomerEvidence
{
    public Guid Id { get; set; }
    public Guid DisputeId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public byte[] Content { get; set; } = [];
    public string? Note { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
}

public sealed class SqliteCustomerEvidenceStore(TransactionDbContext context) : ICustomerEvidenceStore
{
    public async Task<CustomerEvidenceAddOutcome> AddAsync(
        Guid customerId, NewCustomerEvidence evidence, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var dispute = await context.Disputes.AsNoTracking()
            .Where(item => item.Id == evidence.DisputeId && item.CustomerId == customerId)
            .Select(item => new { item.CaseStatus }).SingleOrDefaultAsync(cancellationToken);
        if (dispute is null)
            return CustomerEvidenceAddOutcome.DisputeNotFound;
        if (dispute.CaseStatus == DisputeCaseStatus.Closed)
            return CustomerEvidenceAddOutcome.DisputeClosed;
        if (await context.CustomerEvidence.CountAsync(
                item => item.DisputeId == evidence.DisputeId, cancellationToken)
            >= CustomerEvidenceRules.MaxFilesPerDispute)
            return CustomerEvidenceAddOutcome.LimitReached;

        context.CustomerEvidence.Add(new PersistedCustomerEvidence
        {
            Id = evidence.Id, DisputeId = evidence.DisputeId, FileName = evidence.FileName,
            ContentType = evidence.ContentType, SizeBytes = evidence.Content.Length,
            Sha256 = evidence.Sha256, Content = evidence.Content, Note = evidence.Note,
            UploadedAt = evidence.UploadedAt
        });
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return CustomerEvidenceAddOutcome.Added;
    }

    public async Task<IReadOnlyList<CustomerEvidenceSummary>?> ListAsync(
        Guid? customerId, Guid disputeId, CancellationToken cancellationToken)
    {
        if (!await DisputeExistsAsync(customerId, disputeId, cancellationToken))
            return null;
        // Content is deliberately not selected so a listing never loads file bytes.
        return await context.CustomerEvidence.AsNoTracking()
            .Where(item => item.DisputeId == disputeId)
            .OrderBy(item => item.UploadedAt).ThenBy(item => item.Id)
            .Select(item => new CustomerEvidenceSummary(
                item.Id, item.FileName, item.ContentType, item.SizeBytes, item.UploadedAt, item.Note))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<CustomerEvidenceContent?> ReadContentAsync(
        Guid? customerId, Guid disputeId, Guid evidenceId, CancellationToken cancellationToken)
    {
        if (!await DisputeExistsAsync(customerId, disputeId, cancellationToken))
            return null;
        return await context.CustomerEvidence.AsNoTracking()
            .Where(item => item.Id == evidenceId && item.DisputeId == disputeId)
            .Select(item => new CustomerEvidenceContent(item.FileName, item.ContentType, item.Content))
            .SingleOrDefaultAsync(cancellationToken);
    }

    private Task<bool> DisputeExistsAsync(Guid? customerId, Guid disputeId, CancellationToken cancellationToken) =>
        context.Disputes.AsNoTracking().AnyAsync(
            item => item.Id == disputeId && (customerId == null || item.CustomerId == customerId),
            cancellationToken);
}
