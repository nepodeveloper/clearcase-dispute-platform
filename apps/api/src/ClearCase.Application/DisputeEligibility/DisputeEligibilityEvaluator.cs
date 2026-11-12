using ClearCase.Domain.DisputeEligibility;
using ClearCase.Domain.Transactions;

namespace ClearCase.Application.DisputeEligibility;

/// <summary>Selects the category policy and evaluates it at the current clock instant.</summary>
public sealed class DisputeEligibilityEvaluator(
    IEnumerable<IDisputeEligibilityPolicy> policies,
    DisputeEligibilityPolicySettings settings,
    TimeProvider timeProvider)
{
    public DisputeEligibilityResult Evaluate(DisputeTransaction transaction)
    {
        var policy = policies.SingleOrDefault(item => item.Category == transaction.Category)
            ?? throw new InvalidOperationException("No dispute eligibility policy is registered for the transaction type.");
        return policy.Evaluate(transaction, new DisputeEligibilityContext(timeProvider.GetUtcNow(), settings));
    }
}
