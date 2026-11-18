using ClearCase.Application.AgentDisputes;
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
        var items = await matching
            .Join(context.Transactions.AsNoTracking().Where(item => item.CustomerId == customerId),
                dispute => dispute.TransactionId, transaction => transaction.Id,
                (dispute, transaction) => new { Dispute = dispute, Transaction = transaction })
            .OrderByDescending(row => row.Dispute.CreatedAt).ThenBy(row => row.Dispute.Reference)
            .Select(row => new DisputeListItem(row.Dispute.Id, row.Dispute.Reference,
                row.Dispute.TransactionId, row.Dispute.Category, row.Dispute.CaseStatus,
                row.Dispute.DecisionStatus, row.Dispute.FinancialStatus, row.Dispute.CreatedAt,
                new DisputeTransactionView(row.Transaction.Description, row.Transaction.MerchantName,
                    row.Transaction.AmountMinorUnits, row.Transaction.Currency, row.Transaction.TransactionDate)))
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
        Dispute dispute, DisputeSubmission? submission, CancellationToken cancellationToken,
        OutboxMetadata? outboxMetadata = null)
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
        context.OutboxMessages.Add(PersistedOutboxMessage.Submitted(entity,
            outboxMetadata?.CorrelationId ?? "local"));
        try
        {
            // A single SaveChanges commits the case, timeline, key and event intent together.
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
        if (item is null) return null;
        var transaction = await context.Transactions.AsNoTracking()
            .Where(transaction => transaction.Id == item.TransactionId && transaction.CustomerId == customerId)
            .Select(transaction => new DisputeTransactionView(transaction.Description, transaction.MerchantName,
                transaction.AmountMinorUnits, transaction.Currency, transaction.TransactionDate))
            .SingleAsync(cancellationToken);
        return new DisputeView(
            item.Id, item.Reference, item.TransactionId, item.Category, item.ReasonCode, item.Description,
            item.CaseStatus, item.DecisionStatus, item.FinancialStatus, item.CreatedAt,
            item.Events.OrderBy(entry => entry.SequenceNumber)
                .Select(entry => new DisputeEvent(entry.Id, entry.SequenceNumber, entry.EventType,
                    entry.CaseStatus, entry.PublicDescription, entry.OccurredAt))
                .ToArray(), transaction, item.ReversalOperationReference, item.Chargeback?.ToDomain(),
            item.BankChargeAdjustmentReference, item.Version);
    }

    public Task<DisputeView?> ApplyAsync(
        Guid customerId, Guid disputeId, DebitOrderCommand command,
        Func<DisputeView, Task<DebitOrderTransition>> transition, DateTimeOffset occurredAt,
        CancellationToken cancellationToken, long? expectedVersion = null, string? correlationId = null,
        Guid? employeeId = null) =>
        new DisputeTransitionPersistence(context, this).ExecuteAsync(
            customerId, disputeId, expectedVersion, DisputeTransitionFamily.DebitOrder,
            command.ToString(), occurredAt, correlationId ?? "local", employeeId,
            async current =>
            {
                var next = await transition(current);
                return new DisputeTransitionPlan(next.State.Case, next.State.Decision,
                    next.State.Financial, next.Description,
                    row =>
                    {
                        if (command == DebitOrderCommand.RequestReversal)
                            row.ReversalOperationReference = $"MOCK-DO-{disputeId:N}";
                    });
            }, cancellationToken);

    private static bool IsOpenDisputeConflict(DbUpdateException exception) =>
        exception.InnerException is SqliteException { SqliteErrorCode: SqliteConstraintError } sqlite
        && sqlite.Message.Contains("Disputes.CustomerId, Disputes.TransactionId", StringComparison.Ordinal);

    private static bool IsSubmissionKeyConflict(DbUpdateException exception) =>
        exception.InnerException is SqliteException { SqliteErrorCode: SqliteConstraintError } sqlite
        && sqlite.Message.Contains(
            "DisputeSubmissions.CustomerId, DisputeSubmissions.IdempotencyKey", StringComparison.Ordinal);
}
