using ClearCase.Application.Chargebacks;
using ClearCase.Application.Disputes;
using ClearCase.Domain.Chargebacks;
using ClearCase.Domain.Disputes;

namespace ClearCase.Infrastructure.Disputes;

/// <summary>Recorded provider outcomes become atomic domain transitions; never re-invoke providers.</summary>
public sealed class SqliteRecordedFinancialOutcomeApplier(
    IDebitOrderProcessingStore debitOrders,
    IBankChargeProcessingStore bankCharges,
    IChargebackProcessingStore chargebacks,
    TimeProvider clock) : IRecordedFinancialOutcomeApplier
{
    public async Task ApplyAsync(
        FinancialIntentClaim intent, string outcome, CancellationToken cancellationToken)
    {
        switch (intent.Kind)
        {
            case FinancialOperationKind.DebitReversal:
                var debitOutcome = Enum.Parse<DebitOrderGatewayOutcome>(outcome switch
                {
                    "Unknown" => nameof(DebitOrderGatewayOutcome.Unknown),
                    _ => outcome
                });
                await debitOrders.ApplyAsync(intent.CustomerId, intent.DisputeId,
                    DebitOrderCommand.ExecuteReversal, dispute =>
                    {
                        var state = new DebitOrderState(
                            dispute.CaseStatus, dispute.DecisionStatus, dispute.FinancialStatus);
                        return Task.FromResult(state.CompleteReversal(debitOutcome));
                    }, clock.GetUtcNow(), cancellationToken, intent.ExpectedVersion,
                    intent.CorrelationId, intent.EmployeeId);
                break;
            case FinancialOperationKind.BankChargeAdjustment:
                var bankOutcome = Enum.Parse<BankChargeAdjustmentOutcome>(outcome);
                await bankCharges.ApplyAsync(intent.CustomerId, intent.DisputeId,
                    BankChargeCommand.ExecuteAdjustment, clock.GetUtcNow(), dispute =>
                    {
                        var state = new BankChargeState(
                            dispute.CaseStatus, dispute.DecisionStatus, dispute.FinancialStatus);
                        return Task.FromResult(state.CompleteAdjustment(bankOutcome));
                    }, cancellationToken, intent.ExpectedVersion, intent.CorrelationId, intent.EmployeeId);
                break;
            default:
                var command = CardCommand(intent.Kind);
                var cardOutcome = Enum.Parse<CardNetworkOutcome>(outcome switch
                {
                    "Unknown" => nameof(CardNetworkOutcome.OutcomeUnknown),
                    _ => outcome
                });
                await chargebacks.ApplyAsync(intent.CustomerId,
                    new ChargebackProcessingCommand(intent.DisputeId, command, null, null,
                        intent.CorrelationId, intent.ExpectedVersion),
                    clock.GetUtcNow(), dispute =>
                    {
                        var current = dispute.Chargeback
                            ?? Chargeback.Create(dispute.Id, dispute.ReasonCode);
                        var result = current.Apply(command, dispute.DecisionStatus,
                            dispute.FinancialStatus, null, cardOutcome);
                        return Task.FromResult(result);
                    }, cancellationToken, intent.EmployeeId);
                break;
        }
    }


    private static ChargebackCommand CardCommand(FinancialOperationKind kind) => kind switch
    {
        FinancialOperationKind.CardNetworkInitiate => ChargebackCommand.Initiate,
        FinancialOperationKind.CardNetworkForwardToAcquirer => ChargebackCommand.ForwardToAcquirer,
        FinancialOperationKind.CardNetworkSettlement => ChargebackCommand.ExecuteSettlement,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };


}
