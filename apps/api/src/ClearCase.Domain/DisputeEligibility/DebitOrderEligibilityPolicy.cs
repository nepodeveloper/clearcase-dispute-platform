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
            return Investigation(context, "debitOrder.collectionInformationMissing",
                "More collection information is needed; submit a complaint for investigation.");

        var collectionDate = transaction.CollectionDate.Value;
        var policy = context.Settings.DebitOrder
            .Where(item => item.EffectiveFrom <= collectionDate
                && (item.EffectiveTo is null || item.EffectiveTo >= collectionDate))
            .OrderByDescending(item => item.EffectiveFrom)
            .FirstOrDefault();
        context = context with { PolicyVersion = policy?.PolicyVersion };

        var mandate = transaction.VerifiedMandate;
        if (mandate?.Type is null || !Enum.IsDefined(mandate.Type.Value)
            || mandate.StatusAtCollection is null || !Enum.IsDefined(mandate.StatusAtCollection.Value)
            || mandate.Authorised is null || mandate.AuthorisedAmount is null
            || string.IsNullOrWhiteSpace(mandate.Currency) || mandate.CollectionDate is null)
            return Investigation(context, "debitOrder.mandateInformationMissing",
                "The mandate details need to be confirmed; submit a complaint for investigation.");
        if (!mandate.Authorised.Value)
            return Investigation(context, "debitOrder.unauthorisedCollection",
                "Mandate authorisation is not confirmed; submit a complaint for investigation.");
        if (mandate.StatusAtCollection != DebitOrderMandateStatus.Active)
            return Investigation(context, "debitOrder.mandateNotActive",
                "The mandate was not active at collection; submit a complaint for investigation.");
        if (mandate.AuthorisedAmount != transaction.Amount || mandate.Currency != transaction.Currency
            || mandate.CollectionDate != collectionDate)
            return Investigation(context, "debitOrder.mandateMismatch",
                "The collection amount, currency or date does not match the mandate; submit a complaint for investigation.");
        if (mandate.Type == DebitOrderMandateType.DebiCheck)
            return Investigation(context, "debitOrder.authorisedDebiCheckRequiresReview",
                "This valid authorised DebiCheck collection is ineligible for ordinary reversal and is not automatically reversible. "
                + "If you dispute the authorisation, submit a complaint for investigation.") with
            {
                OrdinaryReversalOutcome = DisputeEligibilityOutcome.Ineligible
            };

        if (policy is null || policy.WindowCalendarDays <= 0 || policy.StandardReviewAmountLimit < 0)
            return EligibilityResult.ManualReview(context, "debitOrder.policyUnavailable",
                "The applicable debit-order review policy needs to be confirmed.");

        // South African collection dates use UTC+02:00, with no daylight-saving transitions.
        var daysAfterCollection = DateOnly.FromDateTime(
            context.EvaluatedAt.ToOffset(TimeSpan.FromHours(2)).DateTime).DayNumber
            - collectionDate.DayNumber;
        if (daysAfterCollection < 1 || daysAfterCollection > policy.WindowCalendarDays)
            return EligibilityResult.ManualReview(context, "debitOrder.outsideStandardWindow",
                "This collection is outside the configured standard window and can be referred for further review.");
        if (transaction.Amount > policy.StandardReviewAmountLimit)
            return EligibilityResult.ManualReview(context, "debitOrder.amountAboveStandardReviewThreshold",
                "This amount needs enhanced review; the amount alone does not prevent you from raising a dispute.");

        return EligibilityResult.Eligible(context);
    }

    private static DisputeEligibilityResult Investigation(
        DisputeEligibilityContext context, string reasonCode, string explanation) =>
        EligibilityResult.ManualReview(context, reasonCode, explanation) with
        {
            ReviewRoute = DisputeReviewRoute.Investigation
        };
}
