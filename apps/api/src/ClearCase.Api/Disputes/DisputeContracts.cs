using System.Text.Json.Serialization;
using ClearCase.Application.Disputes;

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
    IReadOnlyList<DisputeTimelineEventResponse> Timeline)
{
    public static DisputeResponse From(DisputeView view) =>
        new(view.Id, view.Reference, view.TransactionId, view.Category.ToString(), view.ReasonCode,
            view.Description, view.CaseStatus.ToString(), view.DecisionStatus.ToString(),
            view.FinancialStatus.ToString(), view.CreatedAt,
            view.Timeline.Select(item => new DisputeTimelineEventResponse(
                item.Id, item.SequenceNumber, item.EventType, item.CaseStatus.ToString(),
                item.PublicDescription, item.OccurredAt)).ToArray());
}

public sealed record DisputeTimelineEventResponse(
    Guid Id,
    int SequenceNumber,
    string EventType,
    string CaseStatus,
    [property: JsonPropertyName("description")] string PublicDescription,
    DateTimeOffset OccurredAt);
