using System.Text.Json.Serialization;
using ClearCase.Application.SupervisorEscalations;

namespace ClearCase.Api.Disputes;

/// <summary>Agent request to escalate an eligible dispute for independent supervisor review.</summary>
public sealed record CreateDisputeEscalationRequest(string? Reason, string? Note);

/// <summary>Supervisor request to record a private resolution note.</summary>
public sealed record ResolveDisputeEscalationRequest(string? Note);

/// <summary>A continuation page for the supervisor escalation queue.</summary>
public sealed record SupervisorEscalationListResponse(
    IReadOnlyList<SupervisorEscalationListItemResponse> Items,
    SupervisorEscalationPageInfo PageInfo);

/// <summary>Queue metadata for stable cursor pagination.</summary>
public sealed record SupervisorEscalationPageInfo(
    bool HasNextPage,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? NextCursor);

/// <summary>A safe queue row; it contains no customer identifiers or private notes.</summary>
public sealed record SupervisorEscalationListItemResponse(
    Guid Id,
    string Reference,
    Guid DisputeId,
    string DisputeReference,
    string CustomerReference,
    string Reason,
    string Status,
    DateTimeOffset CreatedAt)
{
    public static SupervisorEscalationListItemResponse From(SupervisorEscalationItem item) =>
        new(item.Id, item.Reference, item.DisputeId, item.DisputeReference, item.CustomerReference,
            item.Reason, item.Status.ToString(), item.CreatedAt);
}

/// <summary>
/// Agent-safe response for creation and idempotent replay. The only private note included
/// is the originating Agent's note; Supervisor review history is never serialized.
/// </summary>
public sealed record AgentEscalationCreationResponse(
    Guid Id,
    string Reference,
    Guid DisputeId,
    string Reason,
    string? Note,
    string Status,
    DateTimeOffset CreatedAt)
{
    public static AgentEscalationCreationResponse From(SupervisorEscalationDetails item) =>
        new(item.Id, item.Reference, item.DisputeId, item.Reason,
            item.History.Single(entry => entry.Action == ClearCase.Domain.Disputes.DisputeEscalationAction.Created)
                .PrivateNote,
            item.Status.ToString(), item.CreatedAt);
}

/// <summary>A supervisor-only escalation detail view with append-only review history.</summary>
public sealed record SupervisorEscalationResponse(
    Guid Id,
    string Reference,
    Guid DisputeId,
    string DisputeReference,
    string CustomerReference,
    string Category,
    string CaseStatus,
    string DecisionStatus,
    string FinancialStatus,
    string Reason,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Note,
    string Status,
    Guid CreatedByEmployeeId,
    DateTimeOffset CreatedAt,
    IReadOnlyList<SupervisorEscalationAuditResponse> History)
{
    public static SupervisorEscalationResponse From(SupervisorEscalationDetails item) =>
        new(item.Id, item.Reference, item.DisputeId, item.DisputeReference, item.CustomerReference,
            item.Category.ToString(), item.CaseStatus.ToString(), item.DecisionStatus.ToString(),
            item.FinancialStatus.ToString(), item.Reason,
            item.History.FirstOrDefault(entry => entry.Action == ClearCase.Domain.Disputes.DisputeEscalationAction.Created)
                ?.PrivateNote,
            item.Status.ToString(), item.CreatedByEmployeeId, item.CreatedAt,
            item.History.Select(SupervisorEscalationAuditResponse.From).ToArray());
}

/// <summary>A private escalation action and the employee/correlation audit context.</summary>
public sealed record SupervisorEscalationAuditResponse(
    Guid Id,
    string Action,
    Guid EmployeeId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PrivateNote,
    DateTimeOffset OccurredAt,
    string CorrelationId)
{
    public static SupervisorEscalationAuditResponse From(EscalationAuditEvent item) =>
        new(item.Id, item.Action.ToString(), item.EmployeeId, item.PrivateNote, item.OccurredAt,
            item.CorrelationId);
}
