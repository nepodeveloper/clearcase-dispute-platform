using ClearCase.Application.Transactions;
using ClearCase.Domain.DisputeEligibility;
using ClearCase.Domain.Transactions;

namespace ClearCase.Application.DisputeEligibility;

public interface IGetDisputeEligibilityHandler
{
    Task<DisputeEligibilityResult?> HandleAsync(Guid transactionId, CancellationToken cancellationToken);
}

public sealed class GetDisputeEligibilityHandler(
    ICustomerContext customerContext,
    ITransactionReader transactionReader,
    IEnumerable<IDisputeEligibilityPolicy> policies,
    DisputeEligibilityPolicySettings settings,
    TimeProvider timeProvider) : IGetDisputeEligibilityHandler
{
    public async Task<DisputeEligibilityResult?> HandleAsync(
        Guid transactionId,
        CancellationToken cancellationToken)
    {
        var transaction = await transactionReader.ReadForEligibilityAsync(
            customerContext.CustomerId, transactionId, cancellationToken);
        if (transaction is null)
            return null;

        var policy = policies.SingleOrDefault(item => item.Category == transaction.Category)
            ?? throw new InvalidOperationException("No dispute eligibility policy is registered for the transaction type.");
        return policy.Evaluate(transaction, new DisputeEligibilityContext(timeProvider.GetUtcNow(), settings));
    }
}
