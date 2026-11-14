using ClearCase.Application.Disputes;
using ClearCase.Domain.Disputes;
using ClearCase.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;

namespace ClearCase.Infrastructure.Disputes;

public sealed class MockBankChargeAdjustmentGateway(
    BankChargeAdjustmentOutcome outcome = BankChargeAdjustmentOutcome.Confirmed) : IBankChargeAdjustmentGateway
{
    public Task<BankChargeAdjustmentOutcome> AdjustAsync(string operationReference, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationReference);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(outcome);
    }
}

public sealed class SqliteBankChargeProcessingStore(TransactionDbContext context, IDisputeStore disputes)
    : IBankChargeProcessingStore
{
    public async Task<DisputeView?> ApplyAsync(Guid customerId, Guid disputeId, BankChargeCommand command,
        DateTimeOffset occurredAt, Func<DisputeView, Task<BankChargeTransition>> transition,
        CancellationToken cancellationToken, long? expectedVersion = null, string? correlationId = null)
    {
        // The writer lock serializes local simulations; this gateway must remain free of external side effects.
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var current = await disputes.ReadAsync(customerId, disputeId, cancellationToken);
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
        if (command == BankChargeCommand.RequestAdjustment)
            item.BankChargeAdjustmentReference = $"MOCK-FEE-{disputeId:N}";
        context.DisputeEvents.Add(new PersistedDisputeEvent
        {
            Id = Guid.NewGuid(), DisputeId = disputeId, SequenceNumber = current.Timeline.Max(entry => entry.SequenceNumber) + 1,
            EventType = $"BankCharge{command}", CaseStatus = item.CaseStatus,
            PublicDescription = next.Description, OccurredAt = occurredAt
        });
        context.OutboxMessages.Add(PersistedOutboxMessage.Transitioned(
            item, command.ToString(), occurredAt, "dispute.bank-charge-transitioned.v1",
            correlationId ?? "local"));
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new DisputeConcurrencyConflictException();
        }
        var updated = await disputes.ReadAsync(customerId, disputeId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return updated;
    }
}
