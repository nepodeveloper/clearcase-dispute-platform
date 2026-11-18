using ClearCase.Application.Disputes;
using ClearCase.Domain.Disputes;
using ClearCase.Infrastructure.Transactions;

namespace ClearCase.Infrastructure.Disputes;

public sealed class SqliteBankChargeProcessingStore(
    TransactionDbContext context, IDisputeStore disputes) : IBankChargeProcessingStore
{
    public Task<DisputeView?> ApplyAsync(Guid customerId, Guid disputeId, BankChargeCommand command,
        DateTimeOffset occurredAt, Func<DisputeView, Task<BankChargeTransition>> transition,
        CancellationToken cancellationToken, long? expectedVersion = null, string? correlationId = null,
        Guid? employeeId = null) =>
        new DisputeTransitionPersistence(context, disputes).ExecuteAsync(
            customerId, disputeId, expectedVersion, DisputeTransitionFamily.BankCharge,
            command.ToString(), occurredAt, correlationId ?? "local", employeeId,
            async current =>
            {
                var next = await transition(current);
                return new DisputeTransitionPlan(next.State.Case, next.State.Decision,
                    next.State.Financial, next.Description,
                    row =>
                    {
                        if (command == BankChargeCommand.RequestAdjustment)
                            row.BankChargeAdjustmentReference = $"MOCK-FEE-{disputeId:N}";
                    });
            }, cancellationToken);
}
