using ClearCase.Application.Chargebacks;
using ClearCase.Application.Disputes;
using ClearCase.Domain.Chargebacks;
using ClearCase.Domain.Disputes;
using ClearCase.Domain.Transactions;

namespace ClearCase.Infrastructure.Disputes;

/// <summary>Maps durable card-network intents to the gateway and chargeback transition.</summary>
public sealed class CardNetworkStrategy(
    ICardNetworkGateway gateway,
    IChargebackProcessingStore store,
    TimeProvider clock) : IFinancialOperationStrategy
{
    public bool Supports(FinancialOperationKind kind) =>
        kind is FinancialOperationKind.CardNetworkInitiate
            or FinancialOperationKind.CardNetworkForwardToAcquirer
            or FinancialOperationKind.CardNetworkSettlement;
    public bool IsAvailable => gateway is not UnavailableDisputeProcessingGateway;

    public string OperationReference(Guid disputeId, FinancialOperationKind kind) =>
        $"MOCK-CARD-{disputeId:N}-{CardCommand(kind)}";

    public string EventType(FinancialOperationKind kind) => $"Chargeback{CardCommand(kind)}";

    public void Validate(FinancialOperationDispute dispute, FinancialOperationKind kind)
    {
        var command = CardCommand(kind);
        if (dispute.Category != TransactionCategory.CardPurchase
            || dispute.CaseStatus == DisputeCaseStatus.Closed)
            throw new InvalidChargebackTransitionException();

        var chargeback = dispute.Chargeback
            ?? Chargeback.Create(dispute.DisputeId, dispute.ReasonCode);
        chargeback.Validate(command, dispute.DecisionStatus, dispute.FinancialStatus, null);
    }

    public async Task<string> ExecuteAsync(
        FinancialOperationExecution execution, CancellationToken cancellationToken)
    {
        var command = CardCommand(execution.Kind);
        return (await gateway.ExecuteAsync(
            new CardNetworkRequest(execution.OperationReference,
                $"MOCK-CB-{execution.DisputeId:N}", command), cancellationToken)).ToString();
    }

    public async Task ApplyResultAsync(
        FinancialOperationIntent intent, string outcome, CancellationToken cancellationToken)
    {
        var command = CardCommand(intent.Kind);
        var parsed = Enum.Parse<CardNetworkOutcome>(outcome == "Unknown"
            ? nameof(CardNetworkOutcome.OutcomeUnknown) : outcome);
        await store.ApplyAsync(intent.CustomerId,
            new ChargebackProcessingCommand(intent.DisputeId, command, null, null,
                intent.CorrelationId, intent.ExpectedVersion),
            clock.GetUtcNow(), dispute =>
            {
                var current = dispute.Chargeback
                    ?? Chargeback.Create(dispute.Id, dispute.ReasonCode);
                return Task.FromResult(current.Apply(
                    command, dispute.DecisionStatus, dispute.FinancialStatus, null, parsed));
            }, cancellationToken, intent.EmployeeId);
    }

    private static ChargebackCommand CardCommand(FinancialOperationKind kind) => kind switch
    {
        FinancialOperationKind.CardNetworkInitiate => ChargebackCommand.Initiate,
        FinancialOperationKind.CardNetworkForwardToAcquirer => ChargebackCommand.ForwardToAcquirer,
        FinancialOperationKind.CardNetworkSettlement => ChargebackCommand.ExecuteSettlement,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
