using ClearCase.Application.AgentDisputes;
using ClearCase.Domain.Disputes;
using ClearCase.Infrastructure.Transactions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ClearCase.Infrastructure.Assignments;

public sealed class SqliteDisputeAssignmentStore(TransactionDbContext context) : IDisputeAssignmentStore
{
    private const int SqliteConstraintError = 19;

    public async Task<DisputeAssignmentOutcome> ClaimAsync(
        Guid disputeId, Guid employeeId, string correlationId, DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        if (!await context.Disputes.AsNoTracking().AnyAsync(item => item.Id == disputeId, cancellationToken))
            return DisputeAssignmentOutcome.NotFound;
        var current = await FindAsync(disputeId, cancellationToken);
        if (current is not null)
            return current.EmployeeId == employeeId
                ? DisputeAssignmentOutcome.AlreadyMine : DisputeAssignmentOutcome.AlreadyAssignedToOther;

        context.DisputeAssignments.Add(new PersistedDisputeAssignment
        {
            DisputeId = disputeId, EmployeeId = employeeId, AssignedAt = occurredAt
        });
        context.DisputeAssignmentAuditEvents.Add(Audit(
            disputeId, employeeId, DisputeAssignmentAction.Claimed, null, correlationId, occurredAt));
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return DisputeAssignmentOutcome.Claimed;
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqliteException
            { SqliteErrorCode: SqliteConstraintError })
        {
            context.ChangeTracker.Clear();
            var winner = await FindAsync(disputeId, cancellationToken);
            return winner?.EmployeeId == employeeId
                ? DisputeAssignmentOutcome.AlreadyMine : DisputeAssignmentOutcome.AlreadyAssignedToOther;
        }
    }

    public async Task<DisputeAssignmentOutcome> ReleaseAsync(
        Guid disputeId, Guid actorEmployeeId, bool actorIsSupervisor, string correlationId,
        DateTimeOffset occurredAt, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        if (!await context.Disputes.AsNoTracking().AnyAsync(item => item.Id == disputeId, cancellationToken))
            return DisputeAssignmentOutcome.NotFound;
        var current = await FindAsync(disputeId, cancellationToken);
        if (current is null)
            return DisputeAssignmentOutcome.NotAssigned;
        if (!DisputeAssignmentRules.CanRelease(current.EmployeeId, actorEmployeeId, actorIsSupervisor))
            return DisputeAssignmentOutcome.Forbidden;

        var removed = await context.DisputeAssignments
            .Where(item => item.DisputeId == disputeId && item.EmployeeId == current.EmployeeId)
            .ExecuteDeleteAsync(cancellationToken);
        if (removed == 0)
            return DisputeAssignmentOutcome.NotAssigned;
        context.DisputeAssignmentAuditEvents.Add(Audit(
            disputeId, actorEmployeeId, DisputeAssignmentAction.Released, current.EmployeeId,
            correlationId, occurredAt));
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return DisputeAssignmentOutcome.Released;
    }

    private Task<PersistedDisputeAssignment?> FindAsync(Guid disputeId, CancellationToken cancellationToken) =>
        context.DisputeAssignments.AsNoTracking()
            .SingleOrDefaultAsync(item => item.DisputeId == disputeId, cancellationToken);

    private static PersistedDisputeAssignmentAuditEvent Audit(
        Guid disputeId, Guid employeeId, DisputeAssignmentAction action, Guid? previousEmployeeId,
        string correlationId, DateTimeOffset occurredAt) => new()
    {
        Id = Guid.NewGuid(), DisputeId = disputeId, EmployeeId = employeeId, Action = action,
        PreviousEmployeeId = previousEmployeeId,
        CorrelationId = string.IsNullOrWhiteSpace(correlationId) ? "local" : correlationId,
        OccurredAt = occurredAt
    };
}
