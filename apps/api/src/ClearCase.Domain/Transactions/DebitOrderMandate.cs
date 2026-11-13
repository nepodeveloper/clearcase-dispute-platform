namespace ClearCase.Domain.Transactions;

public enum DebitOrderMandateStatus
{
    Active,
    Cancelled,
    Suspended
}

public sealed record DebitOrderMandate(
    DebitOrderMandateType? Type,
    DebitOrderMandateStatus? StatusAtCollection,
    bool? Authorised,
    decimal? AuthorisedAmount,
    string? Currency,
    DateOnly? CollectionDate);
