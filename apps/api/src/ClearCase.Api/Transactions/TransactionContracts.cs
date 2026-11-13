using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Serialization;
using ClearCase.Application.DisputeEligibility;

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

/// <summary>Eligibility assessment for a transaction, without initiating a dispute or financial operation.</summary>
public sealed record TransactionDisputeEligibilityResponse(
    Guid TransactionId,
    string Outcome,
    IReadOnlyList<string> ReasonCodes,
    string Explanation,
    string PolicyVersion,
    DateTimeOffset EvaluatedAt,
    IReadOnlyList<DisputeReasonResponse> AvailableReasons,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? OrdinaryReversalOutcome = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ReviewRoute = null)
{
    public static TransactionDisputeEligibilityResponse From(
        Guid transactionId,
        DisputeEligibilityAssessment assessment) =>
        new(transactionId, assessment.Result.Outcome.ToString(), assessment.Result.ReasonCodes,
            assessment.Result.Explanation, assessment.Result.PolicyVersion, assessment.Result.EvaluatedAt,
            assessment.AvailableReasons.Select(reason => new DisputeReasonResponse(reason.Code, reason.Label))
                .ToArray(),
            assessment.Result.OrdinaryReversalOutcome?.ToString(), assessment.Result.ReviewRoute?.ToString());
}

/// <summary>A dispute reason the customer may select; empty when the transaction is ineligible.</summary>
public sealed record DisputeReasonResponse(string Code, string Label);
