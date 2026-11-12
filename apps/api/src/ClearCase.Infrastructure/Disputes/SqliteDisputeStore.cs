using ClearCase.Application.Disputes;
using ClearCase.Domain.Disputes;
using ClearCase.Infrastructure.Transactions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ClearCase.Infrastructure.Disputes;

public sealed class SqliteDisputeStore(TransactionDbContext context) : IDisputeStore
{
    private const int SqliteConstraintError = 19;

    public Task<bool> HasOpenDisputeAsync(
        Guid customerId, Guid transactionId, CancellationToken cancellationToken) =>
        context.Disputes.AsNoTracking().AnyAsync(item =>
            item.CustomerId == customerId
            && item.TransactionId == transactionId
            && item.CaseStatus != DisputeCaseStatus.Closed, cancellationToken);

    public async Task<DisputeAddResult> AddAsync(Dispute dispute, CancellationToken cancellationToken)
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
        try
        {
            // A single SaveChanges runs in one database transaction: case and timeline commit together.
            await context.SaveChangesAsync(cancellationToken);
            return DisputeAddResult.Added;
        }
        catch (DbUpdateException exception) when (IsOpenDisputeConflict(exception))
        {
            context.ChangeTracker.Clear();
            return DisputeAddResult.ActiveDisputeExists;
        }
    }

    public async Task<DisputeView?> ReadAsync(
        Guid customerId, Guid disputeId, CancellationToken cancellationToken)
    {
        var item = await context.Disputes.AsNoTracking()
            .Include(dispute => dispute.Events)
            .SingleOrDefaultAsync(dispute => dispute.Id == disputeId && dispute.CustomerId == customerId,
                cancellationToken);
        return item is null ? null : new DisputeView(
            item.Id, item.Reference, item.TransactionId, item.Category, item.ReasonCode, item.Description,
            item.CaseStatus, item.DecisionStatus, item.FinancialStatus, item.CreatedAt,
            item.Events.OrderBy(entry => entry.SequenceNumber)
                .Select(entry => new DisputeEvent(entry.Id, entry.SequenceNumber, entry.EventType,
                    entry.CaseStatus, entry.PublicDescription, entry.OccurredAt))
                .ToArray());
    }

    private static bool IsOpenDisputeConflict(DbUpdateException exception) =>
        exception.InnerException is SqliteException { SqliteErrorCode: SqliteConstraintError } sqlite
        && sqlite.Message.Contains("Disputes.CustomerId, Disputes.TransactionId", StringComparison.Ordinal);
}
