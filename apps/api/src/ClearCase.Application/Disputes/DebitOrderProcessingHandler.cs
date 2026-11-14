using ClearCase.Application.DisputeEligibility;
using ClearCase.Application.Transactions;
using ClearCase.Domain.DisputeEligibility;
using ClearCase.Domain.Disputes;
using ClearCase.Domain.Transactions;

namespace ClearCase.Application.Disputes;

public interface IDebitOrderProcessingGateway
{
    Task<DebitOrderGatewayOutcome> ReverseAsync(string operationReference, CancellationToken cancellationToken);

    Task<DebitOrderGatewayOutcome> GetReversalStatusAsync(
        string operationReference, CancellationToken cancellationToken);
}

public interface IDebitOrderProcessingStore
{
    /// <summary>Serializes the local simulation, committing case, operation reference and timeline together.</summary>
    Task<DisputeView?> ApplyAsync(Guid customerId, Guid disputeId, DebitOrderCommand command,
        Func<DisputeView, Task<DebitOrderTransition>> transition, DateTimeOffset occurredAt,
        CancellationToken cancellationToken, long? expectedVersion = null, string? correlationId = null);
}

public interface IDebitOrderProcessingHandler
{
    Task<DisputeView?> HandleAsync(Guid disputeId, DebitOrderCommand command, CancellationToken cancellationToken,
        long? expectedVersion = null, string? correlationId = null);
}

public sealed class DebitOrderProcessingHandler(
    ICustomerContext customer, IDebitOrderProcessingStore store, ITransactionReader transactions,
    DisputeEligibilityEvaluator evaluator, IDebitOrderProcessingGateway gateway, TimeProvider clock)
    : IDebitOrderProcessingHandler
{
    public Task<DisputeView?> HandleAsync(Guid disputeId, DebitOrderCommand command, CancellationToken cancellationToken,
        long? expectedVersion = null, string? correlationId = null) =>
        store.ApplyAsync(customer.CustomerId, disputeId, command, async dispute =>
        {
            if (dispute.Category != TransactionCategory.DebitOrder)
                throw new InvalidDebitOrderTransitionException();
            var state = new DebitOrderState(dispute.CaseStatus, dispute.DecisionStatus, dispute.FinancialStatus);
            if (command == DebitOrderCommand.ReconcileReversal)
            {
                var reference = dispute.ReversalOperationReference
                    ?? throw new InvalidOperationException("A requested reversal must have an operation reference.");
                return state.ReconcileReversal(
                    await gateway.GetReversalStatusAsync(reference, cancellationToken));
            }

            var transaction = await transactions.ReadForEligibilityAsync(
                customer.CustomerId, dispute.TransactionId, cancellationToken);
            var eligible = transaction is not null
                && (await evaluator.EvaluateAsync(customer.CustomerId, transaction, cancellationToken)).Outcome
                    == DisputeEligibilityOutcome.EligibleToSubmit;
            var transition = state.Apply(command, eligible, dispute.ReasonCode != "duplicateCollection");
            if (command == DebitOrderCommand.ExecuteReversal)
            {
                var reference = dispute.ReversalOperationReference
                    ?? throw new InvalidOperationException("A requested reversal must have an operation reference.");
                transition = state.CompleteReversal(await gateway.ReverseAsync(reference, cancellationToken));
            }
            return transition;
        }, clock.GetUtcNow(), cancellationToken, expectedVersion, correlationId);
}
