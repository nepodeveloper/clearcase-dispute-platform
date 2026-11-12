using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Serialization;

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

/// <summary>Identifies one invalid query parameter.</summary>
public sealed record QueryError(string Pointer, string Code, string Message);

/// <summary>Details of an owned synthetic transaction; amount is a decimal string.</summary>
public sealed record TransactionDetailsResponse(
    Guid Id,
    string TransactionType,
    string Description,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? MerchantName,
    string Amount,
    string Currency,
    DateTimeOffset TransactionDate,
    string Status,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Reference,
    bool IsDisputable);
