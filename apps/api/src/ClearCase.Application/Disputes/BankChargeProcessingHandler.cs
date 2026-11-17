using ClearCase.Application.DisputeEligibility;
using ClearCase.Application.AgentDisputes;
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
        CancellationToken cancellationToken, long? expectedVersion = null, string? correlationId = null,
        Guid? employeeId = null);
}

public interface IBankChargeProcessingHandler
{
    Task<DisputeView?> HandleAsync(Guid disputeId, BankChargeCommand command, CancellationToken cancellationToken,
        long? expectedVersion = null, string? correlationId = null, EmployeeDisputeScope? employee = null);
}

public sealed class BankChargeProcessingHandler(
    ICustomerContext customer, IBankChargeProcessingStore store, ITransactionReader transactions,
    DisputeEligibilityEvaluator evaluator, IFinancialOperationCoordinator operations, TimeProvider clock)
    : IBankChargeProcessingHandler
{
    public Task<DisputeView?> HandleAsync(Guid disputeId, BankChargeCommand command, CancellationToken cancellationToken,
        long? expectedVersion = null, string? correlationId = null, EmployeeDisputeScope? employee = null)
    {
        var customerId = employee?.CustomerId ?? customer.CustomerId;
        if (command == BankChargeCommand.ExecuteAdjustment)
            return operations.ExecuteAsync(new FinancialOperationRequest(
                customerId, disputeId, FinancialOperationKind.BankChargeAdjustment,
                expectedVersion, employee?.EmployeeId, correlationId ?? "local"), cancellationToken);

        return store.ApplyAsync(customerId, disputeId, command, clock.GetUtcNow(), async dispute =>
        {
            if (dispute.Category != TransactionCategory.BankCharge)
                throw new InvalidBankChargeTransitionException();
            var transaction = await transactions.ReadForEligibilityAsync(customerId, dispute.TransactionId, cancellationToken);
            var eligibility = transaction is null ? null
                : await evaluator.EvaluateAsync(customerId, transaction, cancellationToken);
            var ready = eligibility is not null && eligibility.Outcome != DisputeEligibilityOutcome.Ineligible
                && eligibility.ReasonCodes.All(code => code == "bankCharge.configuredReviewCondition");
            var state = new BankChargeState(dispute.CaseStatus, dispute.DecisionStatus, dispute.FinancialStatus);
            return state.Apply(command, ready);
        }, cancellationToken, expectedVersion, correlationId, employee?.EmployeeId);
    }
}
