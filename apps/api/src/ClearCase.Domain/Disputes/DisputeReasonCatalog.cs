using ClearCase.Domain.Transactions;

namespace ClearCase.Domain.Disputes;

public sealed record DisputeReason(string Code, string Label);

/// <summary>Demonstration reason codes per category; not card-network or scheme reason codes.</summary>
public static class DisputeReasonCatalog
{
    private static readonly IReadOnlyDictionary<TransactionCategory, IReadOnlyList<DisputeReason>> Reasons =
        new Dictionary<TransactionCategory, IReadOnlyList<DisputeReason>>
        {
            [TransactionCategory.CardPurchase] =
            [
                new("unrecognisedTransaction", "I do not recognise this transaction"),
                new("duplicateCharge", "I was charged more than once"),
                new("goodsOrServicesNotReceived", "I did not receive the goods or services"),
                new("incorrectAmount", "The amount charged is incorrect")
            ],
            [TransactionCategory.DebitOrder] =
            [
                new("unauthorisedDebitOrder", "I did not authorise this debit order"),
                new("amountDiffersFromMandate", "The amount differs from what I agreed"),
                new("mandateCancelled", "I cancelled this debit order"),
                new("duplicateCollection", "This debit order was collected more than once")
            ],
            [TransactionCategory.BankCharge] =
            [
                new("incorrectFee", "This fee was charged incorrectly"),
                new("duplicateFee", "This fee was charged more than once"),
                new("feeNotAgreed", "I did not agree to this fee")
            ]
        };

    public static IReadOnlyList<DisputeReason> For(TransactionCategory category) =>
        Reasons.TryGetValue(category, out var reasons) ? reasons : [];

    public static bool IsValid(TransactionCategory category, string reasonCode) =>
        For(category).Any(reason => string.Equals(reason.Code, reasonCode, StringComparison.Ordinal));
}
