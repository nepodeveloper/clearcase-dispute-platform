using ClearCase.Application.Disputes;
using ClearCase.Domain.Disputes;
using ClearCase.Domain.Transactions;

namespace ClearCase.Infrastructure.Disputes;

/// <summary>Maps durable debit-order intents to the external gateway and local transition.</summary>
public sealed class DebitReversalStrategy(
    IDebitOrderProcessingGateway gateway,
    IDebitOrderProcessingStore store,
    TimeProvider clock) : IFinancialOperationStrategy
{
    public bool Supports(FinancialOperationKind kind) => kind == FinancialOperationKind.DebitReversal;
    public bool IsAvailable => gateway is not UnavailableDisputeProcessingGateway;

    public string OperationReference(Guid disputeId, FinancialOperationKind kind)
    {
        EnsureSupported(kind);
        return $"MOCK-DO-{disputeId:N}";
    }

    public string EventType(FinancialOperationKind kind)
    {
        EnsureSupported(kind);
        return "DebitOrderExecuteReversal";
    }

    public void Validate(FinancialOperationDispute dispute, FinancialOperationKind kind)
    {
        EnsureSupported(kind);
        if (dispute.Category != TransactionCategory.DebitOrder)
            throw new InvalidDebitOrderTransitionException();

        new DebitOrderState(dispute.CaseStatus, dispute.DecisionStatus, dispute.FinancialStatus)
            .Apply(DebitOrderCommand.ExecuteReversal, true, false);
    }

    public async Task<string> ExecuteAsync(
        FinancialOperationExecution execution, CancellationToken cancellationToken)
    {
        EnsureSupported(execution.Kind);
        return (await gateway.ReverseAsync(
            execution.OperationReference, cancellationToken)).ToString();
    }

    public async Task ApplyResultAsync(
        FinancialOperationIntent intent, string outcome, CancellationToken cancellationToken)
    {
        var parsed = Enum.Parse<DebitOrderGatewayOutcome>(outcome == "Unknown"
            ? nameof(DebitOrderGatewayOutcome.Unknown) : outcome);
        await store.ApplyAsync(intent.CustomerId, intent.DisputeId,
            DebitOrderCommand.ExecuteReversal, dispute =>
            {
                var state = new DebitOrderState(
                    dispute.CaseStatus, dispute.DecisionStatus, dispute.FinancialStatus);
                return Task.FromResult(state.CompleteReversal(parsed));
            }, clock.GetUtcNow(), cancellationToken, intent.ExpectedVersion,
            intent.CorrelationId, intent.EmployeeId);
    }

    private static void EnsureSupported(FinancialOperationKind kind)
    {
        if (kind != FinancialOperationKind.DebitReversal)
            throw new ArgumentOutOfRangeException(nameof(kind));
    }
}
