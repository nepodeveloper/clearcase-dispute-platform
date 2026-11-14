using ClearCase.Application.DisputeEligibility;
using ClearCase.Application.Transactions;
using ClearCase.Domain.DisputeEligibility;
using ClearCase.Domain.Disputes;
using ClearCase.Domain.Transactions;

namespace ClearCase.Application.Disputes;

public interface IBankChargeAdjustmentGateway
{
    Task<BankChargeAdjustmentOutcome> AdjustAsync(string operationReference, CancellationToken cancellationToken);
}

public interface IBankChargeProcessingStore
{
    Task<DisputeView?> ApplyAsync(Guid customerId, Guid disputeId, BankChargeCommand command,
        DateTimeOffset occurredAt, Func<DisputeView, Task<BankChargeTransition>> transition,
        CancellationToken cancellationToken, long? expectedVersion = null, string? correlationId = null);
}

public interface IBankChargeProcessingHandler
{
    Task<DisputeView?> HandleAsync(Guid disputeId, BankChargeCommand command, CancellationToken cancellationToken,
        long? expectedVersion = null, string? correlationId = null);
}

public sealed class BankChargeProcessingHandler(
    ICustomerContext customer, IBankChargeProcessingStore store, ITransactionReader transactions,
    DisputeEligibilityEvaluator evaluator, IBankChargeAdjustmentGateway gateway, TimeProvider clock)
    : IBankChargeProcessingHandler
{
    public Task<DisputeView?> HandleAsync(Guid disputeId, BankChargeCommand command, CancellationToken cancellationToken,
        long? expectedVersion = null, string? correlationId = null) =>
        store.ApplyAsync(customer.CustomerId, disputeId, command, clock.GetUtcNow(), async dispute =>
        {
            if (dispute.Category != TransactionCategory.BankCharge)
                throw new InvalidBankChargeTransitionException();
            var transaction = await transactions.ReadForEligibilityAsync(customer.CustomerId, dispute.TransactionId, cancellationToken);
            var eligibility = transaction is null ? null
                : await evaluator.EvaluateAsync(customer.CustomerId, transaction, cancellationToken);
            var ready = eligibility is not null && eligibility.Outcome != DisputeEligibilityOutcome.Ineligible
                && eligibility.ReasonCodes.All(code => code == "bankCharge.configuredReviewCondition");
            var state = new BankChargeState(dispute.CaseStatus, dispute.DecisionStatus, dispute.FinancialStatus);
            var change = state.Apply(command, ready);
            if (command == BankChargeCommand.ExecuteAdjustment)
            {
                var reference = dispute.BankChargeAdjustmentReference
                    ?? throw new InvalidOperationException("A requested fee adjustment must have an operation reference.");
                change = state.CompleteAdjustment(await gateway.AdjustAsync(reference, cancellationToken));
            }
            return change;
        }, cancellationToken, expectedVersion, correlationId);
}
