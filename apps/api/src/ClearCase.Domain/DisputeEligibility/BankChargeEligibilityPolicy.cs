using ClearCase.Domain.Transactions;

namespace ClearCase.Domain.DisputeEligibility;

public sealed class BankChargeEligibilityPolicy : IDisputeEligibilityPolicy
{
    public TransactionCategory Category => TransactionCategory.BankCharge;

    public DisputeEligibilityResult Evaluate(
        DisputeTransaction transaction,
        DisputeEligibilityContext context)
    {
        if (!context.Settings.DemoPoliciesEnabled)
            return EligibilityResult.ManualReview(context, "policy.notConfigured",
                "The applicable bank-charge review policy needs to be confirmed.");
        if (transaction.Category != Category)
            return EligibilityResult.Ineligible(context, "bankCharge.unsupportedType",
                "This bank-charge policy does not apply to this transaction type.");
        if (transaction.Status is not (TransactionStatus.Posted or TransactionStatus.Settled))
            return EligibilityResult.Ineligible(context, "bankCharge.notPosted",
                "This bank charge has not been posted or settled.");
        if (!transaction.HasRequiredDisputeInformation)
            return EligibilityResult.ManualReview(context, "transaction.informationMissing",
                "More transaction information is needed before this fee can be reviewed.");
        if (transaction.Amount <= 0)
            return EligibilityResult.ManualReview(context, "bankCharge.invalidAmount",
                "The bank-charge amount needs to be confirmed before review.");
        if (context.Settings.BankCharge.ManualReviewAboveAmount is decimal threshold
            && transaction.Amount > threshold)
            return EligibilityResult.ManualReview(context, "bankCharge.configuredReviewCondition",
                "This bank charge needs further internal review.");

        return EligibilityResult.Eligible(context);
    }
}
