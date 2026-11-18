using ClearCase.Application.DisputeEligibility;
using ClearCase.Application.AgentDisputes;
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
        CancellationToken cancellationToken, long? expectedVersion = null, string? correlationId = null,
        Guid? employeeId = null);
}

public interface IDebitOrderProcessingHandler
{
    Task<DisputeView?> HandleAsync(Guid disputeId, DebitOrderCommand command, CancellationToken cancellationToken,
        long? expectedVersion = null, string? correlationId = null, EmployeeDisputeScope? employee = null);
}

public sealed class DebitOrderProcessingHandler(
    ICustomerContext customer, IDebitOrderProcessingStore store, IDisputeStore disputeReader,
    ITransactionReader transactions,
    DisputeEligibilityEvaluator evaluator, IDebitOrderProcessingGateway gateway,
    IFinancialOperationCoordinator operations, TimeProvider clock)
    : IDebitOrderProcessingHandler
{
    public Task<DisputeView?> HandleAsync(Guid disputeId, DebitOrderCommand command, CancellationToken cancellationToken,
        long? expectedVersion = null, string? correlationId = null, EmployeeDisputeScope? employee = null)
    {
        var customerId = employee?.CustomerId ?? customer.CustomerId;
        if (command == DebitOrderCommand.ExecuteReversal)
            return ExecuteCheckedAsync(customerId, disputeId, expectedVersion,
                employee?.EmployeeId, correlationId ?? "local", cancellationToken);
        if (command == DebitOrderCommand.ReconcileReversal)
            return ReconcileCheckedAsync(customerId, disputeId, expectedVersion,
                employee?.EmployeeId, correlationId ?? "local", cancellationToken);

        return store.ApplyAsync(customerId, disputeId, command, async dispute =>
        {
            if (dispute.Category != TransactionCategory.DebitOrder)
                throw new InvalidDebitOrderTransitionException();
            var state = new DebitOrderState(dispute.CaseStatus, dispute.DecisionStatus, dispute.FinancialStatus);
            var transaction = await transactions.ReadForEligibilityAsync(
                customerId, dispute.TransactionId, cancellationToken);
            var eligible = transaction is not null
                && (await evaluator.EvaluateAsync(customerId, transaction, cancellationToken)).Outcome
                    == DisputeEligibilityOutcome.EligibleToSubmit;
            return state.Apply(command, eligible, dispute.ReasonCode != "duplicateCollection");
        }, clock.GetUtcNow(), cancellationToken, expectedVersion, correlationId, employee?.EmployeeId);
    }

    private async Task<DisputeView?> ReconcileCheckedAsync(
        Guid customerId, Guid disputeId, long? expectedVersion, Guid? employeeId,
        string correlationId, CancellationToken cancellationToken)
    {
        var dispute = await disputeReader.ReadAsync(customerId, disputeId, cancellationToken);
        if (dispute is null) return null;
        if (dispute.Category != TransactionCategory.DebitOrder)
            throw new InvalidDebitOrderTransitionException();
        if (expectedVersion is { } requestedVersion && requestedVersion != dispute.Version)
            throw new DisputeConcurrencyConflictException();

        var operationReference = dispute.ReversalOperationReference
            ?? throw new InvalidOperationException("A requested reversal must have an operation reference.");
        var outcome = await gateway.GetReversalStatusAsync(operationReference, cancellationToken);
        return await store.ApplyAsync(customerId, disputeId, DebitOrderCommand.ReconcileReversal, current =>
        {
            if (current.Category != TransactionCategory.DebitOrder
                || current.ReversalOperationReference != operationReference)
                throw new DisputeConcurrencyConflictException();
            var state = new DebitOrderState(
                current.CaseStatus, current.DecisionStatus, current.FinancialStatus);
            return Task.FromResult(state.ReconcileReversal(outcome));
        }, clock.GetUtcNow(), cancellationToken, expectedVersion ?? dispute.Version,
        correlationId, employeeId);
    }

    private async Task<DisputeView?> ExecuteCheckedAsync(
        Guid customerId, Guid disputeId, long? expectedVersion, Guid? employeeId,
        string correlationId, CancellationToken cancellationToken)
    {
        var dispute = await disputeReader.ReadAsync(customerId, disputeId, cancellationToken);
        if (dispute is null) return null;
        if (dispute.Category != TransactionCategory.DebitOrder)
            throw new InvalidDebitOrderTransitionException();
        var source = await transactions.ReadForEligibilityAsync(customerId, dispute.TransactionId, cancellationToken);
        if (source is null || (await evaluator.EvaluateAsync(customerId, source, cancellationToken)).Outcome
                != DisputeEligibilityOutcome.EligibleToSubmit)
            throw new InvalidDebitOrderTransitionException();
        return await operations.ExecuteAsync(new FinancialOperationRequest(customerId, disputeId,
            FinancialOperationKind.DebitReversal, expectedVersion, employeeId, correlationId), cancellationToken);
    }
}
