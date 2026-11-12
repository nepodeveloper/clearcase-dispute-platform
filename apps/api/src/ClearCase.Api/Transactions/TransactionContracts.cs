using Microsoft.AspNetCore.Mvc;

namespace ClearCase.Api.Transactions;

/// <summary>Raw query parameters for a transaction listing.</summary>
public sealed record TransactionListRequest
{
    [FromQuery(Name = "pageNumber")] public string? PageNumber { get; init; }
    [FromQuery(Name = "pageSize")] public string? PageSize { get; init; }
    [FromQuery(Name = "transactionType")] public string? TransactionType { get; init; }
    [FromQuery(Name = "fromDate")] public string? FromDate { get; init; }
    [FromQuery(Name = "toDate")] public string? ToDate { get; init; }
}

public enum TransactionType { CardPurchase, DebitOrder, BankCharge }

public sealed record TransactionQuery(
    int PageNumber, int PageSize, TransactionType? Type, DateOnly? FromDate, DateOnly? ToDate);

/// <summary>A synthetic transaction; amount is a decimal string in the given currency.</summary>
public sealed record TransactionResponse(
    Guid Id, string TransactionType, string Description, string Amount, string Currency,
    DateTimeOffset TransactionDate, string Status, bool IsDisputable);

/// <summary>A filtered page of synthetic transactions.</summary>
public sealed record TransactionListResponse(
    IReadOnlyList<TransactionResponse> Items, int PageNumber, int PageSize,
    int TotalCount, int TotalPages, bool HasNextPage);

/// <summary>Identifies one invalid query parameter.</summary>
public sealed record QueryError(string Pointer, string Code, string Message);
