using ClearCase.Domain.Transactions;

namespace ClearCase.Domain.DisputeEligibility;

public sealed class CardPurchaseEligibilityPolicy : IDisputeEligibilityPolicy
{
    public TransactionCategory Category => TransactionCategory.CardPurchase;

    public DisputeEligibilityResult Evaluate(
        DisputeTransaction transaction,
        DisputeEligibilityContext context)
    {
        if (!context.Settings.DemoPoliciesEnabled)
            return EligibilityResult.ManualReview(context, "policy.notConfigured",
                "The applicable card review policy needs to be confirmed.");
        if (transaction.Category != Category)
            return EligibilityResult.Ineligible(context, "card.unsupportedType",
                "This card policy does not apply to this transaction type.");
        if (!transaction.IsDebit)
            return EligibilityResult.Ineligible(context, "transaction.notDebit",
                "Only posted debit transactions can be submitted through this review path.");
        if (transaction.Status == TransactionStatus.PendingAuthorization)
            return EligibilityResult.Ineligible(context, "card.pendingAuthorization",
                "This card transaction is still a pending authorization.");
        if (transaction.Status is not (TransactionStatus.Posted or TransactionStatus.Settled))
            return EligibilityResult.Ineligible(context, "transaction.notPosted",
                "This transaction has not been posted or settled.");
        if (transaction.Amount <= 0)
            return EligibilityResult.ManualReview(context, "card.invalidAmount",
                "The transaction amount needs to be confirmed before review.");
        if (!transaction.HasRequiredDisputeInformation)
            return EligibilityResult.ManualReview(context, "transaction.informationMissing",
                "More transaction information is needed before this request can be reviewed.");

        var cardSettings = context.Settings.Card;
        if (!cardSettings.DemoWindowEnabled || cardSettings.DemoWindowCalendarDays < 0)
            return EligibilityResult.ManualReview(context, "card.windowNotConfigured",
                "The applicable card review window needs to be confirmed.");

        var elapsedDays = DateOnly.FromDateTime(context.EvaluatedAt.UtcDateTime).DayNumber
            - DateOnly.FromDateTime(transaction.TransactionDate.UtcDateTime).DayNumber;
        if (elapsedDays < 0)
            return EligibilityResult.ManualReview(context, "card.transactionDateInFuture",
                "The transaction date needs to be confirmed before review.");
        if (elapsedDays > cardSettings.DemoWindowCalendarDays)
            return EligibilityResult.Ineligible(context, "card.outsideDemoWindow",
                "This transaction is outside the configured demonstration review window.");

        return EligibilityResult.Eligible(context);
    }
}
