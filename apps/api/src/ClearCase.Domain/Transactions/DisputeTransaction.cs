namespace ClearCase.Domain.Transactions;

public enum TransactionCategory
{
    CardPurchase,
    DebitOrder,
    BankCharge
}

public enum TransactionStatus
{
    PendingAuthorization,
    Posted,
    Settled
}

public enum DebitOrderMandateType
{
    EftDebitOrder,
    RegisteredMandate,
    DebiCheck
}

public sealed record DisputeTransaction(
    Guid Id,
    TransactionCategory Category,
    decimal Amount,
    string Currency,
    DateTimeOffset TransactionDate,
    TransactionStatus Status,
    bool IsDebit,
    bool HasRequiredDisputeInformation,
    DateOnly? CollectionDate,
    DebitOrderMandateType? MandateType,
    bool? MandateAuthorised,
    bool? MandateMatchesCollection,
    DebitOrderMandate? VerifiedMandate = null);
