using System.Text.Json.Serialization;

namespace ClearCase.Application.Transactions;

public enum TransactionType { CardPurchase, DebitOrder, BankCharge }

public sealed record ListTransactionsQuery(
    int PageNumber, int PageSize, TransactionType? Type, DateOnly? FromDate, DateOnly? ToDate,
    string? Search = null, TransactionCursor? After = null);

public sealed record TransactionResponse(
    Guid Id, string TransactionType, string Description, string Amount, string Currency,
    DateTimeOffset TransactionDate, string Status, bool IsDisputable,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? ActiveDisputeId = null);

public sealed record TransactionPageInfo(
    bool HasNextPage,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? NextCursor);

/// <summary>pageNumber, totalCount and totalPages remain for compatibility; pageInfo is the cursor-based contract.</summary>
public sealed record TransactionListResponse(
    IReadOnlyList<TransactionResponse> Items, int PageNumber, int PageSize,
    int TotalCount, int TotalPages, bool HasNextPage,
    [property: JsonIgnore] string? NextCursor = null)
{
    public TransactionPageInfo PageInfo => new(HasNextPage, NextCursor);
}

public sealed record TransactionDetails(
    Guid Id, string TransactionType, string Description, string? MerchantName, decimal Amount,
    string Currency, DateTimeOffset TransactionDate, string Status, string? Reference, bool IsDisputable,
    Guid? ActiveDisputeId = null);
