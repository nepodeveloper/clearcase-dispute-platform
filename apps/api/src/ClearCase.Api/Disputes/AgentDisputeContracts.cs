using System.Text.Json.Serialization;
using System.Globalization;
using ClearCase.Application.AgentDisputes;

namespace ClearCase.Api.Disputes;

public sealed record AgentDisputeListResponse(
    IReadOnlyList<AgentDisputeListItemResponse> Items,
    AgentPageInfo PageInfo);

public sealed record AgentPageInfo(
    bool HasNextPage,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? NextCursor);

public sealed record AgentDisputeListItemResponse(
    Guid Id,
    string Reference,
    string CustomerReference,
    string Category,
    string TransactionDescription,
    string AmountMinorUnits,
    string Currency,
    string ReasonCode,
    string CaseStatus,
    string DecisionStatus,
    string FinancialStatus,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastActivityAt,
    long Version)
{
    public static AgentDisputeListItemResponse From(AgentDisputeListItem item) =>
        new(item.Id, item.Reference, item.CustomerReference, item.Category.ToString(),
            item.TransactionDescription, item.AmountMinorUnits.ToString(CultureInfo.InvariantCulture),
            item.Currency, item.ReasonCode,
            item.CaseStatus.ToString(), item.DecisionStatus.ToString(), item.FinancialStatus.ToString(),
            item.CreatedAt, item.LastActivityAt, item.Version);
}

public sealed record AgentDisputeDetailResponse(
    Guid Id,
    string Reference,
    string CustomerReference,
    string Category,
    string ReasonCode,
    string CustomerStatement,
    string EligibilityOutcome,
    string EligibilityPolicyVersion,
    string CaseStatus,
    string DecisionStatus,
    string FinancialStatus,
    DateTimeOffset CreatedAt,
    long Version,
    AgentTransactionResponse Transaction,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ReversalOperationReference,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? BankChargeAdjustmentReference,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] AgentCardWorkflowResponse? CardWorkflow,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] AgentEscalationResponse? ActiveEscalation,
    IReadOnlyList<AgentTimelineEventResponse> Timeline,
    IReadOnlyList<InvestigationNoteResponse> Notes)
{
    public static AgentDisputeDetailResponse From(AgentDisputeDetails detail) =>
        new(detail.Id, detail.Reference, detail.CustomerReference, detail.Category.ToString(),
            detail.ReasonCode, detail.CustomerStatement, detail.EligibilityOutcome.ToString(),
            detail.EligibilityPolicyVersion, detail.CaseStatus.ToString(), detail.DecisionStatus.ToString(),
            detail.FinancialStatus.ToString(), detail.CreatedAt, detail.Version,
            AgentTransactionResponse.From(detail.Transaction), detail.ReversalOperationReference,
            detail.BankChargeAdjustmentReference,
            detail.CardWorkflow is { } workflow ? AgentCardWorkflowResponse.From(workflow) : null,
            detail.ActiveEscalation is { } escalation ? AgentEscalationResponse.From(escalation) : null,
            detail.Timeline.Select(AgentTimelineEventResponse.From).ToArray(),
            detail.Notes.Select(InvestigationNoteResponse.From).ToArray());
}

public sealed record AgentEscalationResponse(
    Guid Id,
    string Reference,
    string Reason,
    string Status,
    DateTimeOffset CreatedAt)
{
    public static AgentEscalationResponse From(AgentEscalationSummary escalation) =>
        new(escalation.Id, escalation.Reference, escalation.Reason, escalation.Status, escalation.CreatedAt);
}

public sealed record AgentTransactionResponse(
    string Description,
    string AmountMinorUnits,
    string Currency,
    DateTimeOffset TransactionDate,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? MandateType,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateOnly? CollectionDate,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? MandateAuthorised,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? MandateMatchesCollection)
{
    public static AgentTransactionResponse From(AgentTransactionDetails transaction) =>
        new(transaction.Description, transaction.AmountMinorUnits.ToString(CultureInfo.InvariantCulture), transaction.Currency,
            transaction.TransactionDate, transaction.MandateType, transaction.CollectionDate,
            transaction.MandateAuthorised, transaction.MandateMatchesCollection);
}

public sealed record AgentCardWorkflowResponse(
    string Reference,
    string ReasonCode,
    string Status,
    string LastOperationOutcome)
{
    public static AgentCardWorkflowResponse From(AgentCardWorkflow workflow) =>
        new(workflow.Reference, workflow.ReasonCode, workflow.Status, workflow.LastOperationOutcome);
}

public sealed record AgentTimelineEventResponse(
    Guid Id,
    int SequenceNumber,
    string EventType,
    string CaseStatus,
    string Description,
    DateTimeOffset OccurredAt)
{
    public static AgentTimelineEventResponse From(AgentTimelineEvent item) =>
        new(item.Id, item.SequenceNumber, item.EventType, item.CaseStatus.ToString(),
            item.PublicDescription, item.OccurredAt);
}

public sealed record InvestigationNoteResponse(
    Guid Id,
    Guid DisputeId,
    Guid EmployeeId,
    string Text,
    DateTimeOffset CreatedAt,
    string CorrelationId)
{
    public static InvestigationNoteResponse From(InvestigationNote note) =>
        new(note.Id, note.DisputeId, note.EmployeeId, note.Text, note.CreatedAt, note.CorrelationId);
}

public sealed record CreateInvestigationNoteRequest(string? Text);
