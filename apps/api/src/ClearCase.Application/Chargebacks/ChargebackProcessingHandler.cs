using ClearCase.Application.DisputeEligibility;
using ClearCase.Application.Disputes;
using ClearCase.Application.Transactions;
using ClearCase.Domain.Chargebacks;
using ClearCase.Domain.DisputeEligibility;
using ClearCase.Domain.Disputes;
using ClearCase.Domain.Transactions;

namespace ClearCase.Application.Chargebacks;

public sealed record CardNetworkRequest(
    string OperationReference, string ChargebackReference, ChargebackCommand Command);

public interface ICardNetworkGateway
{
    Task<CardNetworkOutcome> ExecuteAsync(CardNetworkRequest request, CancellationToken cancellationToken);
}

public sealed record ChargebackProcessingCommand(
    Guid DisputeId, ChargebackCommand Command, Guid? EvidenceId, ChargebackEvidenceKind? EvidenceKind,
    string CorrelationId, long? ExpectedVersion = null);

public interface IChargebackProcessingStore
{
    Task<DisputeView?> ApplyAsync(Guid customerId, ChargebackProcessingCommand command,
        DateTimeOffset occurredAt, Func<DisputeView, Task<ChargebackTransition>> transition,
        CancellationToken cancellationToken);
}

public interface IChargebackProcessingHandler
{
    Task<DisputeView?> HandleAsync(ChargebackProcessingCommand command, CancellationToken cancellationToken);
}

public sealed class ChargebackProcessingHandler(
    ICustomerContext customer, IChargebackProcessingStore store, ITransactionReader transactions,
    DisputeEligibilityEvaluator evaluator, ICardNetworkGateway gateway, TimeProvider clock)
    : IChargebackProcessingHandler
{
    public Task<DisputeView?> HandleAsync(ChargebackProcessingCommand command, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var evidence = command.EvidenceId is { } id && command.EvidenceKind is { } kind
            ? new RepresentmentEvidence(id, kind, now) : null;
        return store.ApplyAsync(customer.CustomerId, command, now, async dispute =>
        {
            if (dispute.Category != TransactionCategory.CardPurchase || dispute.CaseStatus == DisputeCaseStatus.Closed)
                throw new InvalidChargebackTransitionException();
            var chargeback = dispute.Chargeback;
            if (chargeback is null)
            {
                if (command.Command != ChargebackCommand.Initiate
                    || dispute.CaseStatus is not (DisputeCaseStatus.Submitted or DisputeCaseStatus.ManualReview))
                    throw new InvalidChargebackTransitionException();
                var transaction = await transactions.ReadForEligibilityAsync(
                    customer.CustomerId, dispute.TransactionId, cancellationToken);
                if (transaction is null || (await evaluator.EvaluateAsync(
                        customer.CustomerId, transaction, cancellationToken)).Outcome != DisputeEligibilityOutcome.EligibleToSubmit)
                    throw new InvalidChargebackTransitionException();
                chargeback = Chargeback.Create(dispute.Id, dispute.ReasonCode);
            }
            chargeback.Validate(command.Command, dispute.DecisionStatus, dispute.FinancialStatus, evidence);
            var outcome = Chargeback.UsesGateway(command.Command)
                ? await gateway.ExecuteAsync(new CardNetworkRequest(
                    chargeback.OperationReference(command.Command), chargeback.Reference, command.Command), cancellationToken)
                : CardNetworkOutcome.Succeeded;
            return chargeback.Apply(command.Command, dispute.DecisionStatus, dispute.FinancialStatus, evidence, outcome);
        }, cancellationToken);
    }
}
