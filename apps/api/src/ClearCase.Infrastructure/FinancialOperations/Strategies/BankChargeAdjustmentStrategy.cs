using ClearCase.Application.Disputes;
using ClearCase.Domain.Disputes;
using ClearCase.Domain.Transactions;

namespace ClearCase.Infrastructure.Disputes;

/// <summary>Maps durable bank-charge intents to the adjustment gateway and local transition.</summary>
public sealed class BankChargeAdjustmentStrategy(
    IBankChargeAdjustmentGateway gateway,
    IBankChargeProcessingStore store,
    TimeProvider clock) : IFinancialOperationStrategy
{
    public bool Supports(FinancialOperationKind kind) =>
        kind == FinancialOperationKind.BankChargeAdjustment;
    public bool IsAvailable => gateway is not UnavailableDisputeProcessingGateway;

    public string OperationReference(Guid disputeId, FinancialOperationKind kind)
    {
        EnsureSupported(kind);
        return $"MOCK-FEE-{disputeId:N}";
    }

    public string EventType(FinancialOperationKind kind)
    {
        EnsureSupported(kind);
        return "BankChargeExecuteAdjustment";
    }

    public void Validate(FinancialOperationDispute dispute, FinancialOperationKind kind)
    {
        EnsureSupported(kind);
        if (dispute.Category != TransactionCategory.BankCharge)
            throw new InvalidBankChargeTransitionException();

        new BankChargeState(dispute.CaseStatus, dispute.DecisionStatus, dispute.FinancialStatus)
            .Apply(BankChargeCommand.ExecuteAdjustment, true);
    }

    public async Task<string> ExecuteAsync(
        FinancialOperationExecution execution, CancellationToken cancellationToken)
    {
        EnsureSupported(execution.Kind);
        return (await gateway.AdjustAsync(
            execution.OperationReference, cancellationToken)).ToString();
    }

    public async Task ApplyResultAsync(
        FinancialOperationIntent intent, string outcome, CancellationToken cancellationToken)
    {
        var parsed = Enum.Parse<BankChargeAdjustmentOutcome>(outcome);
        await store.ApplyAsync(intent.CustomerId, intent.DisputeId,
            BankChargeCommand.ExecuteAdjustment, clock.GetUtcNow(), dispute =>
            {
                var state = new BankChargeState(
                    dispute.CaseStatus, dispute.DecisionStatus, dispute.FinancialStatus);
                return Task.FromResult(state.CompleteAdjustment(parsed));
            }, cancellationToken, intent.ExpectedVersion, intent.CorrelationId, intent.EmployeeId);
    }

    private static void EnsureSupported(FinancialOperationKind kind)
    {
        if (kind != FinancialOperationKind.BankChargeAdjustment)
            throw new ArgumentOutOfRangeException(nameof(kind));
    }
}
