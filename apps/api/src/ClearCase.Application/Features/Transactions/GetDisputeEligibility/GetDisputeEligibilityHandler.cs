using ClearCase.Application.Transactions;
using ClearCase.Domain.DisputeEligibility;
using ClearCase.Domain.Disputes;

namespace ClearCase.Application.DisputeEligibility;

/// <summary>An eligibility decision plus the reasons a customer may select when it is not ineligible.</summary>
public sealed record DisputeEligibilityAssessment(
    DisputeEligibilityResult Result,
    IReadOnlyList<DisputeReason> AvailableReasons);

public interface IGetDisputeEligibilityHandler
{
    Task<DisputeEligibilityAssessment?> HandleAsync(Guid transactionId, CancellationToken cancellationToken);
}

public sealed class GetDisputeEligibilityHandler(
    ICustomerContext customerContext,
    ITransactionReader transactionReader,
    DisputeEligibilityEvaluator evaluator) : IGetDisputeEligibilityHandler
{
    public async Task<DisputeEligibilityAssessment?> HandleAsync(
        Guid transactionId,
        CancellationToken cancellationToken)
    {
        var customerId = customerContext.CustomerId;
        var transaction = await transactionReader.ReadForEligibilityAsync(
            customerId, transactionId, cancellationToken);
        if (transaction is null)
            return null;

        var result = await evaluator.EvaluateAsync(customerId, transaction, cancellationToken);
        return new DisputeEligibilityAssessment(result,
            result.Outcome == DisputeEligibilityOutcome.Ineligible
                ? []
                : DisputeReasonCatalog.For(transaction.Category));
    }
}
