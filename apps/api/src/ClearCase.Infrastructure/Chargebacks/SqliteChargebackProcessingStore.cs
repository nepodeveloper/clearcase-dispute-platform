using ClearCase.Application.Chargebacks;
using ClearCase.Application.Disputes;
using ClearCase.Domain.Chargebacks;
using ClearCase.Infrastructure.Disputes;
using ClearCase.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;

namespace ClearCase.Infrastructure.Chargebacks;

public sealed class SqliteChargebackProcessingStore(TransactionDbContext context, IDisputeStore disputes)
    : IChargebackProcessingStore
{
    public async Task<DisputeView?> ApplyAsync(
        Guid customerId, ChargebackProcessingCommand command, DateTimeOffset occurredAt,
        Func<DisputeView, Task<ChargebackTransition>> transition, CancellationToken cancellationToken)
    {
        // Only a pure mock may run under this SQLite writer lock; external banking cannot join this transaction.
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var current = await disputes.ReadAsync(customerId, command.DisputeId, cancellationToken);
        if (current is null) return null;
        if (command.ExpectedVersion is { } expected && current.Version != expected)
            throw new DisputeConcurrencyConflictException();
        var next = await transition(current);
        var item = await context.Disputes.Include(item => item.Chargeback).SingleAsync(
            item => item.Id == command.DisputeId && item.CustomerId == customerId, cancellationToken);
        item.Chargeback ??= new PersistedChargeback();
        item.Chargeback.Update(next.Chargeback);
        item.CaseStatus = next.CaseStatus;
        item.DecisionStatus = next.DecisionStatus;
        item.FinancialStatus = next.FinancialStatus;
        item.Version = checked(item.Version + 1);
        context.DisputeEvents.Add(new PersistedDisputeEvent
        {
            Id = Guid.NewGuid(), DisputeId = item.Id, SequenceNumber = current.Timeline.Max(entry => entry.SequenceNumber) + 1,
            EventType = $"Chargeback{command.Command}", CaseStatus = item.CaseStatus,
            PublicDescription = next.Description, OccurredAt = occurredAt
        });
        context.ChargebackAuditEvents.Add(new ChargebackAuditEvent
        {
            Id = Guid.NewGuid(), DisputeId = item.Id, OperationReference = next.Chargeback.OperationReference(command.Command),
            Command = command.Command, ChargebackStatus = next.Chargeback.Status,
            DecisionStatus = item.DecisionStatus, FinancialStatus = item.FinancialStatus,
            Outcome = next.Chargeback.LastOperationOutcome, ActorId = customerId, CorrelationId = command.CorrelationId,
            EvidenceId = next.Chargeback.Evidence?.Id, EvidenceKind = next.Chargeback.Evidence?.Kind, OccurredAt = occurredAt
        });
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new DisputeConcurrencyConflictException();
        }
        var updated = await disputes.ReadAsync(customerId, command.DisputeId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return updated;
    }
}
