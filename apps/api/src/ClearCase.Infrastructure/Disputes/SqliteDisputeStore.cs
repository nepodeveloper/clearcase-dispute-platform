using ClearCase.Application.Disputes;
using ClearCase.Domain.Disputes;
using ClearCase.Infrastructure.Transactions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ClearCase.Infrastructure.Disputes;

public sealed class SqliteDisputeStore(TransactionDbContext context) : IDisputeStore, IDebitOrderProcessingStore
{
    private const int SqliteConstraintError = 19;

    public async Task<DisputeListPage> ReadPageAsync(
        Guid customerId, ListDisputesQuery query, CancellationToken cancellationToken)
    {
        var matching = context.Disputes.AsNoTracking().Where(item => item.CustomerId == customerId);
        if (query.After is { } cursor)
            matching = matching.Where(item => item.CreatedAt < cursor.CreatedAt
                || (item.CreatedAt == cursor.CreatedAt && string.Compare(item.Reference, cursor.Reference) > 0));
        var items = await matching.OrderByDescending(item => item.CreatedAt).ThenBy(item => item.Reference)
            .Select(item => new DisputeListItem(item.Id, item.Reference, item.TransactionId, item.Category,
                item.CaseStatus, item.DecisionStatus, item.FinancialStatus, item.CreatedAt))
            .Take(query.PageSize + 1).ToArrayAsync(cancellationToken);
        return new(items.Take(query.PageSize).ToArray(), items.Length > query.PageSize);
    }

    public Task<bool> HasOpenDisputeAsync(
        Guid customerId, Guid transactionId, CancellationToken cancellationToken) =>
        context.Disputes.AsNoTracking().AnyAsync(item =>
            item.CustomerId == customerId
            && item.TransactionId == transactionId
            && item.CaseStatus != DisputeCaseStatus.Closed, cancellationToken);

    public Task<DisputeAddResult> AddAsync(Dispute dispute, CancellationToken cancellationToken) =>
        AddAsync(dispute, null, cancellationToken);

    public async Task<DisputeAddResult> AddAsync(
        Dispute dispute, DisputeSubmission? submission, CancellationToken cancellationToken)
    {
        var entity = new PersistedDispute
        {
            Id = dispute.Id,
            Reference = dispute.Reference,
            CustomerId = dispute.CustomerId,
            TransactionId = dispute.TransactionId,
            Category = dispute.Category,
            ReasonCode = dispute.ReasonCode,
            Description = dispute.Description,
            CaseStatus = dispute.CaseStatus,
            DecisionStatus = dispute.DecisionStatus,
            FinancialStatus = dispute.FinancialStatus,
            EligibilityOutcome = dispute.Eligibility.Outcome,
            EligibilityPolicyVersion = dispute.Eligibility.PolicyVersion,
            CreatedAt = dispute.CreatedAt,
            Events = dispute.Timeline.Select(item => new PersistedDisputeEvent
            {
                Id = item.Id,
                DisputeId = dispute.Id,
                SequenceNumber = item.SequenceNumber,
                EventType = item.EventType,
                CaseStatus = item.CaseStatus,
                PublicDescription = item.PublicDescription,
                OccurredAt = item.OccurredAt
            }).ToList()
        };
        context.Disputes.Add(entity);
        if (submission is not null)
            context.DisputeSubmissions.Add(new PersistedDisputeSubmission
            {
                CustomerId = dispute.CustomerId,
                IdempotencyKey = submission.IdempotencyKey,
                RequestFingerprint = submission.RequestFingerprint,
                DisputeId = dispute.Id,
                CreatedAt = dispute.CreatedAt
            });
        try
        {
            // A single SaveChanges runs in one database transaction: case, timeline and key commit together.
            await context.SaveChangesAsync(cancellationToken);
            return DisputeAddResult.Added;
        }
        catch (DbUpdateException exception) when (IsOpenDisputeConflict(exception))
        {
            context.ChangeTracker.Clear();
            return DisputeAddResult.ActiveDisputeExists;
        }
        catch (DbUpdateException exception) when (IsSubmissionKeyConflict(exception))
        {
            context.ChangeTracker.Clear();
            return DisputeAddResult.SubmissionKeyExists;
        }
    }

    public Task<StoredDisputeSubmission?> FindSubmissionAsync(
        Guid customerId, string idempotencyKey, CancellationToken cancellationToken) =>
        context.DisputeSubmissions.AsNoTracking()
            .Where(item => item.CustomerId == customerId && item.IdempotencyKey == idempotencyKey)
            .Select(item => new StoredDisputeSubmission(item.DisputeId, item.RequestFingerprint))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<DisputeView?> ReadAsync(
        Guid customerId, Guid disputeId, CancellationToken cancellationToken)
    {
        var item = await context.Disputes.AsNoTracking()
            .Include(dispute => dispute.Events)
            .Include(dispute => dispute.Chargeback)
            .SingleOrDefaultAsync(dispute => dispute.Id == disputeId && dispute.CustomerId == customerId,
                cancellationToken);
        return item is null ? null : new DisputeView(
            item.Id, item.Reference, item.TransactionId, item.Category, item.ReasonCode, item.Description,
            item.CaseStatus, item.DecisionStatus, item.FinancialStatus, item.CreatedAt,
            item.Events.OrderBy(entry => entry.SequenceNumber)
                .Select(entry => new DisputeEvent(entry.Id, entry.SequenceNumber, entry.EventType,
                    entry.CaseStatus, entry.PublicDescription, entry.OccurredAt))
                .ToArray(), item.ReversalOperationReference, item.Chargeback?.ToDomain(),
            item.BankChargeAdjustmentReference, item.Version);
    }

    public async Task<DisputeView?> ApplyAsync(
        Guid customerId, Guid disputeId, DebitOrderCommand command,
        Func<DisputeView, Task<DebitOrderTransition>> transition, DateTimeOffset occurredAt,
        CancellationToken cancellationToken, long? expectedVersion = null)
    {
        // SQLite's non-deferred transaction takes the writer lock before reading or calling the pure mock.
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var current = await ReadAsync(customerId, disputeId, cancellationToken);
        if (current is null) return null;
        if (expectedVersion is { } expected && current.Version != expected)
            throw new DisputeConcurrencyConflictException();
        var next = await transition(current);
        var item = await context.Disputes.SingleAsync(
            item => item.Id == disputeId && item.CustomerId == customerId, cancellationToken);
        item.CaseStatus = next.State.Case;
        item.DecisionStatus = next.State.Decision;
        item.FinancialStatus = next.State.Financial;
        item.Version = checked(item.Version + 1);
        if (command == DebitOrderCommand.RequestReversal)
            item.ReversalOperationReference = $"MOCK-DO-{disputeId:N}";
        context.DisputeEvents.Add(new PersistedDisputeEvent
        {
            Id = Guid.NewGuid(), DisputeId = disputeId,
            SequenceNumber = current.Timeline.Max(entry => entry.SequenceNumber) + 1,
            EventType = $"DebitOrder{command}", CaseStatus = item.CaseStatus,
            PublicDescription = next.Description, OccurredAt = occurredAt
        });
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new DisputeConcurrencyConflictException();
        }
        var updated = await ReadAsync(customerId, disputeId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return updated;
    }

    private static bool IsOpenDisputeConflict(DbUpdateException exception) =>
        exception.InnerException is SqliteException { SqliteErrorCode: SqliteConstraintError } sqlite
        && sqlite.Message.Contains("Disputes.CustomerId, Disputes.TransactionId", StringComparison.Ordinal);

    private static bool IsSubmissionKeyConflict(DbUpdateException exception) =>
        exception.InnerException is SqliteException { SqliteErrorCode: SqliteConstraintError } sqlite
        && sqlite.Message.Contains(
            "DisputeSubmissions.CustomerId, DisputeSubmissions.IdempotencyKey", StringComparison.Ordinal);
}
