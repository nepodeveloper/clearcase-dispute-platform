using ClearCase.Application.Chargebacks;
using ClearCase.Application.Disputes;
using ClearCase.Domain.Chargebacks;

namespace ClearCase.Infrastructure.Disputes;

/// <summary>Gateway strategies perform external I/O only; they never own persistence or retries.</summary>
public sealed class DebitReversalStrategy(IDebitOrderProcessingGateway gateway)
    : IFinancialOperationStrategy
{
    public bool Supports(FinancialOperationKind kind) => kind == FinancialOperationKind.DebitReversal;
    public bool IsAvailable => gateway is not UnavailableDisputeProcessingGateway;

    public async Task<string> ExecuteAsync(FinancialOperationExecution execution, CancellationToken cancellationToken)
    {
        if (!Supports(execution.Kind))
            throw new ArgumentOutOfRangeException(nameof(execution));
        return (await gateway.ReverseAsync(execution.OperationReference, cancellationToken)).ToString();
    }
}

public sealed class BankChargeAdjustmentStrategy(IBankChargeAdjustmentGateway gateway)
    : IFinancialOperationStrategy
{
    public bool Supports(FinancialOperationKind kind) => kind == FinancialOperationKind.BankChargeAdjustment;
    public bool IsAvailable => gateway is not UnavailableDisputeProcessingGateway;

    public async Task<string> ExecuteAsync(FinancialOperationExecution execution, CancellationToken cancellationToken)
    {
        if (!Supports(execution.Kind))
            throw new ArgumentOutOfRangeException(nameof(execution));
        return (await gateway.AdjustAsync(execution.OperationReference, cancellationToken)).ToString();
    }
}

public sealed class CardNetworkStrategy(ICardNetworkGateway gateway)
    : IFinancialOperationStrategy
{
    public bool Supports(FinancialOperationKind kind) =>
        kind is FinancialOperationKind.CardNetworkInitiate
            or FinancialOperationKind.CardNetworkForwardToAcquirer
            or FinancialOperationKind.CardNetworkSettlement;
    public bool IsAvailable => gateway is not UnavailableDisputeProcessingGateway;

    public async Task<string> ExecuteAsync(FinancialOperationExecution execution, CancellationToken cancellationToken)
    {
        var command = execution.Kind switch
        {
            FinancialOperationKind.CardNetworkInitiate => ChargebackCommand.Initiate,
            FinancialOperationKind.CardNetworkForwardToAcquirer => ChargebackCommand.ForwardToAcquirer,
            FinancialOperationKind.CardNetworkSettlement => ChargebackCommand.ExecuteSettlement,
            _ => throw new ArgumentOutOfRangeException(nameof(execution))
        };
        return (await gateway.ExecuteAsync(
            new CardNetworkRequest(execution.OperationReference,
                $"MOCK-CB-{execution.DisputeId:N}", command), cancellationToken)).ToString();
    }
}
