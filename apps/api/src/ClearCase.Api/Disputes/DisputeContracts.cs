using System.Globalization;
using System.Text.Json.Serialization;
using ClearCase.Application.Disputes;
using ClearCase.Domain.Chargebacks;
using ClearCase.Domain.Disputes;

namespace ClearCase.Api.Disputes;

/// <summary>Customer request to open a dispute; the customer is derived from authentication, never the body.</summary>
public sealed record CreateDisputeRequest(string? TransactionId, string? ReasonCode, string? Description);

/// <summary>A customer-owned dispute case with its immutable timeline.</summary>
public sealed record DisputeResponse(
    Guid Id,
    string Reference,
    Guid TransactionId,
    string Category,
    string ReasonCode,
    string Description,
    string CaseStatus,
    string DecisionStatus,
    string FinancialStatus,
    DateTimeOffset CreatedAt,
    long Version,
    string StatusDescription,
    IReadOnlyList<DisputeTimelineEventResponse> Timeline,
    DisputedTransactionResponse Transaction,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ChargebackResponse? Chargeback = null)
{
    public static DisputeResponse From(DisputeView view) =>
        new(view.Id, view.Reference, view.TransactionId, view.Category.ToString(), view.ReasonCode,
            view.Description, view.CaseStatus.ToString(), view.DecisionStatus.ToString(),
            view.FinancialStatus.ToString(), view.CreatedAt, view.Version,
            DescribeStatus(view.CaseStatus, view.DecisionStatus, view.FinancialStatus),
            view.Timeline.Select(DisputeTimelineEventResponse.From).ToArray(),
            DisputedTransactionResponse.From(view.Transaction),
            view.Chargeback is { } chargeback ? ChargebackResponse.From(chargeback) : null);

    internal static string DescribeStatus(DisputeCaseStatus status, DisputeDecisionStatus decision, DisputeFinancialStatus financial) =>
        financial switch
        {
            DisputeFinancialStatus.OutcomeUnknown => "The payment outcome still needs checking; funds are not confirmed.",
            DisputeFinancialStatus.Failed => "The payment could not be completed. No funds are confirmed.",
            DisputeFinancialStatus.Requested => "A payment adjustment was requested; funds are not confirmed.",
            DisputeFinancialStatus.Confirmed => "The demo payment adjustment is complete. No real money moved.",
            _ => decision == DisputeDecisionStatus.Approved ? "Your dispute is approved. A payment adjustment has not been started."
                : status switch
                {
                    DisputeCaseStatus.ManualReview => "Your case needs specialist investigation.",
                    DisputeCaseStatus.UnderReview => "Your case is being investigated.",
                    DisputeCaseStatus.MandateVerified => "Payment authorization checks are complete. No refund has been requested.",
                    DisputeCaseStatus.Rejected => "Your dispute was not approved after review. No payment adjustment was made.",
                    DisputeCaseStatus.Closed => "Your case is closed. No payment adjustment was made.",
                    _ => "Your dispute was received and is awaiting review."
                }
        };
}

/// <summary>Owned transaction context for identifying a dispute; money remains an exact decimal string.</summary>
public sealed record DisputedTransactionResponse(
    string Description,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? MerchantName,
    string Amount, string Currency, DateTimeOffset TransactionDate)
{
    public static DisputedTransactionResponse From(DisputeTransactionView transaction) =>
        new(transaction.Description, transaction.MerchantName,
            (transaction.AmountMinorUnits / 100m).ToString("0.00", CultureInfo.InvariantCulture),
            transaction.Currency, transaction.TransactionDate);
}

/// <summary>Customer-safe chargeback progress; approval and settlement remain separate dispute fields.</summary>
public sealed record ChargebackResponse(
    string Reference, string ReasonCode, string Status, string LastOperationOutcome,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RepresentmentEvidenceResponse? Evidence)
{
    public static ChargebackResponse From(Chargeback chargeback) =>
        new(chargeback.Reference, chargeback.ReasonCode, chargeback.Status.ToString(), chargeback.LastOperationOutcome.ToString(),
            chargeback.Evidence is { } evidence
                ? new RepresentmentEvidenceResponse(evidence.Id, evidence.Kind.ToString(), evidence.ReceivedAt) : null);
}

/// <summary>Synthetic evidence metadata only; no document content, filename or storage URL is exposed.</summary>
public sealed record RepresentmentEvidenceResponse(Guid Id, string Kind, DateTimeOffset ReceivedAt);

/// <summary>An immutable customer-safe event; sequence number defines chronological order, including tied timestamps.</summary>
public sealed record DisputeTimelineEventResponse(
    Guid Id,
    int SequenceNumber,
    string EventType,
    string CaseStatus,
    [property: JsonPropertyName("description")] string PublicDescription,
    DateTimeOffset OccurredAt)
{
    public static DisputeTimelineEventResponse From(DisputeEvent item) =>
        new(item.Id, item.SequenceNumber, item.EventType, item.CaseStatus.ToString(), item.PublicDescription, item.OccurredAt);
}
