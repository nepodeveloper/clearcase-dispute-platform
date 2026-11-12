using ClearCase.Domain.Transactions;

namespace ClearCase.Domain.DisputeEligibility;

public sealed class DebitOrderEligibilityPolicy : IDisputeEligibilityPolicy
{
    public TransactionCategory Category => TransactionCategory.DebitOrder;

    public DisputeEligibilityResult Evaluate(
        DisputeTransaction transaction,
        DisputeEligibilityContext context)
    {
        if (!context.Settings.DemoPoliciesEnabled)
            return EligibilityResult.ManualReview(context, "policy.notConfigured",
                "The applicable debit-order review policy needs to be confirmed.");
        if (transaction.Category != Category)
            return EligibilityResult.Ineligible(context, "debitOrder.unsupportedType",
                "This debit-order policy does not apply to this transaction type.");
        if (!transaction.IsDebit)
            return EligibilityResult.Ineligible(context, "debitOrder.notDebit",
                "Only debit transactions can be reviewed as debit orders.");
        if (transaction.Status is not (TransactionStatus.Posted or TransactionStatus.Settled))
            return EligibilityResult.Ineligible(context, "debitOrder.notPosted",
                "This debit order has not been posted or settled.");
        if (transaction.Amount <= 0)
            return EligibilityResult.ManualReview(context, "debitOrder.invalidAmount",
                "The debit-order amount needs to be confirmed before review.");
        if (!transaction.HasRequiredDisputeInformation || transaction.CollectionDate is null)
            return EligibilityResult.ManualReview(context, "debitOrder.collectionInformationMissing",
                "More collection information is needed before this request can be reviewed.");

        var collectionDate = transaction.CollectionDate.Value;
        var policy = context.Settings.DebitOrder
            .Where(item => item.EffectiveFrom <= collectionDate
                && (item.EffectiveTo is null || item.EffectiveTo >= collectionDate))
            .OrderByDescending(item => item.EffectiveFrom)
            .FirstOrDefault();
        if (policy is null || policy.WindowCalendarDays <= 0 || policy.StandardReviewAmountLimit < 0)
            return EligibilityResult.ManualReview(context, "debitOrder.policyUnavailable",
                "The applicable debit-order review policy needs to be confirmed.");

        if (transaction.MandateType is null
            || transaction.MandateAuthorised is null
            || transaction.MandateMatchesCollection is null)
            return EligibilityResult.ManualReview(context, "debitOrder.mandateInformationMissing",
                "The mandate details need to be confirmed before this request can be reviewed.");
        if (!transaction.MandateAuthorised.Value)
            return EligibilityResult.ManualReview(context, "debitOrder.unauthorisedCollection",
                "This collection needs further review because mandate authorisation is not confirmed.");
        if (!transaction.MandateMatchesCollection.Value)
            return EligibilityResult.ManualReview(context, "debitOrder.mandateMismatch",
                "This collection needs further review because the mandate details do not match.");
        if (transaction.MandateType == DebitOrderMandateType.DebiCheck)
            return EligibilityResult.ManualReview(context, "debitOrder.authorisedDebiCheckRequiresReview",
                "This authorised DebiCheck collection requires further review and is not automatically reversible.");

        var daysAfterCollection = DateOnly.FromDateTime(context.EvaluatedAt.UtcDateTime).DayNumber
            - collectionDate.DayNumber;
        if (daysAfterCollection < 1 || daysAfterCollection > policy.WindowCalendarDays)
            return EligibilityResult.ManualReview(context, "debitOrder.outsideStandardWindow",
                "This collection is outside the configured standard window and can be referred for further review.");
        if (transaction.Amount > policy.StandardReviewAmountLimit)
            return EligibilityResult.ManualReview(context, "debitOrder.amountAboveStandardReviewThreshold",
                "This amount needs enhanced review; the amount alone does not prevent you from raising a dispute.");

        return EligibilityResult.Eligible(context);
    }
}
