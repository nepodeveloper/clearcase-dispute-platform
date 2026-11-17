using ClearCase.Application.DisputeEligibility;
using ClearCase.Application.AgentDisputes;
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
        CancellationToken cancellationToken, Guid? employeeId = null);
}

public interface IChargebackProcessingHandler
{
    Task<DisputeView?> HandleAsync(ChargebackProcessingCommand command, CancellationToken cancellationToken,
        EmployeeDisputeScope? employee = null);
}

public sealed class ChargebackProcessingHandler(
    ICustomerContext customer, IChargebackProcessingStore store, IDisputeStore disputeReader,
    ITransactionReader transactions,
    DisputeEligibilityEvaluator evaluator, IFinancialOperationExecutor operations, TimeProvider clock)
    : IChargebackProcessingHandler
{
    public Task<DisputeView?> HandleAsync(ChargebackProcessingCommand command, CancellationToken cancellationToken,
        EmployeeDisputeScope? employee = null)
    {
        var customerId = employee?.CustomerId ?? customer.CustomerId;
        var now = clock.GetUtcNow();
        var evidence = command.EvidenceId is { } id && command.EvidenceKind is { } kind
            ? new RepresentmentEvidence(id, kind, now) : null;
        if (Chargeback.UsesGateway(command.Command))
        {
            var financialKind = command.Command switch
            {
                ChargebackCommand.Initiate => FinancialOperationKind.CardNetworkInitiate,
                ChargebackCommand.ForwardToAcquirer => FinancialOperationKind.CardNetworkForwardToAcquirer,
                ChargebackCommand.ExecuteSettlement => FinancialOperationKind.CardNetworkSettlement,
                _ => throw new InvalidChargebackTransitionException()
            };
            return ExecuteCheckedAsync(customerId, command, financialKind,
                employee?.EmployeeId, cancellationToken);
        }

        return store.ApplyAsync(customerId, command, now, async dispute =>
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
                    customerId, dispute.TransactionId, cancellationToken);
                if (transaction is null || (await evaluator.EvaluateAsync(
                        customerId, transaction, cancellationToken)).Outcome != DisputeEligibilityOutcome.EligibleToSubmit)
                    throw new InvalidChargebackTransitionException();
                chargeback = Chargeback.Create(dispute.Id, dispute.ReasonCode);
            }
            chargeback.Validate(command.Command, dispute.DecisionStatus, dispute.FinancialStatus, evidence);
            return chargeback.Apply(command.Command, dispute.DecisionStatus, dispute.FinancialStatus,
                evidence, CardNetworkOutcome.Succeeded);
        }, cancellationToken, employee?.EmployeeId);
    }

    private async Task<DisputeView?> ExecuteCheckedAsync(
        Guid customerId, ChargebackProcessingCommand command,
        FinancialOperationKind kind, Guid? employeeId, CancellationToken cancellationToken)
    {
        var dispute = await disputeReader.ReadAsync(customerId, command.DisputeId, cancellationToken);
        if (dispute is null) return null;
        if (dispute.Category != TransactionCategory.CardPurchase || dispute.CaseStatus == DisputeCaseStatus.Closed)
            throw new InvalidChargebackTransitionException();
        if (command.Command == ChargebackCommand.Initiate)
        {
            var source = await transactions.ReadForEligibilityAsync(customerId, dispute.TransactionId, cancellationToken);
            if (source is null || (await evaluator.EvaluateAsync(customerId, source, cancellationToken)).Outcome
                    != DisputeEligibilityOutcome.EligibleToSubmit)
                throw new InvalidChargebackTransitionException();
        }
        return await operations.ExecuteAsync(new FinancialOperationRequest(customerId, command.DisputeId, kind,
            command.ExpectedVersion, employeeId, command.CorrelationId), cancellationToken);
    }
}
