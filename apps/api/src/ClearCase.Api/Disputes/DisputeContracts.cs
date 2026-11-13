using System.Text.Json.Serialization;
using ClearCase.Application.Disputes;
using ClearCase.Domain.Chargebacks;

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
    IReadOnlyList<DisputeTimelineEventResponse> Timeline,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ChargebackResponse? Chargeback = null)
{
    public static DisputeResponse From(DisputeView view) =>
        new(view.Id, view.Reference, view.TransactionId, view.Category.ToString(), view.ReasonCode,
            view.Description, view.CaseStatus.ToString(), view.DecisionStatus.ToString(),
            view.FinancialStatus.ToString(), view.CreatedAt,
            view.Timeline.Select(item => new DisputeTimelineEventResponse(
                item.Id, item.SequenceNumber, item.EventType, item.CaseStatus.ToString(),
                item.PublicDescription, item.OccurredAt)).ToArray(),
            view.Chargeback is { } chargeback ? ChargebackResponse.From(chargeback) : null);
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

public sealed record DisputeTimelineEventResponse(
    Guid Id,
    int SequenceNumber,
    string EventType,
    string CaseStatus,
    [property: JsonPropertyName("description")] string PublicDescription,
    DateTimeOffset OccurredAt);
