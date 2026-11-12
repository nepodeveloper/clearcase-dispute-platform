namespace ClearCase.Application.Transactions;

public enum TransactionType { CardPurchase, DebitOrder, BankCharge }

public sealed record ListTransactionsQuery(
    int PageNumber, int PageSize, TransactionType? Type, DateOnly? FromDate, DateOnly? ToDate);

public sealed record TransactionResponse(
    Guid Id, string TransactionType, string Description, string Amount, string Currency,
    DateTimeOffset TransactionDate, string Status, bool IsDisputable);

public sealed record TransactionListResponse(
    IReadOnlyList<TransactionResponse> Items, int PageNumber, int PageSize,
    int TotalCount, int TotalPages, bool HasNextPage);

public sealed record TransactionDetails(
    Guid Id, string TransactionType, string Description, string? MerchantName, decimal Amount,
    string Currency, DateTimeOffset TransactionDate, string Status, string? Reference, bool IsDisputable);
