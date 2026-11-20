using System.Security.Cryptography;
using System.Text;
using ClearCase.Domain.DisputeEligibility;
using ClearCase.Domain.Disputes;
using ClearCase.Domain.Transactions;

namespace ClearCase.Application.AgentDisputes;

public sealed record AgentDisputeCursor(DateTimeOffset CreatedAt, string Reference);

public enum AgentAssignmentFilter { All, Mine, Unassigned }

public sealed record AgentAssignee(Guid EmployeeId, string DisplayName);

public sealed record ListAgentDisputesQuery(
    int PageSize,
    string? Search,
    TransactionCategory? Category,
    DisputeCaseStatus? CaseStatus,
    AgentDisputeCursor? After,
    AgentAssignmentFilter Assignment = AgentAssignmentFilter.All,
    Guid? EmployeeId = null);

public sealed record AgentDisputeListItem(
    Guid Id,
    string Reference,
    string CustomerReference,
    TransactionCategory Category,
    string TransactionDescription,
    long AmountMinorUnits,
    string Currency,
    string ReasonCode,
    DisputeCaseStatus CaseStatus,
    DisputeDecisionStatus DecisionStatus,
    DisputeFinancialStatus FinancialStatus,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastActivityAt,
    long Version,
    AgentAssignee? AssignedTo = null,
    DisputeSlaAssessment? Sla = null);

public sealed record AgentDisputePage(IReadOnlyList<AgentDisputeListItem> Items, bool HasNextPage);

public sealed record AgentTransactionDetails(
    string Description,
    long AmountMinorUnits,
    string Currency,
    DateTimeOffset TransactionDate,
    string? MandateType,
    DateOnly? CollectionDate,
    bool? MandateAuthorised,
    bool? MandateMatchesCollection);

public sealed record AgentTimelineEvent(
    Guid Id,
    int SequenceNumber,
    string EventType,
    DisputeCaseStatus CaseStatus,
    string PublicDescription,
    DateTimeOffset OccurredAt);

public sealed record AgentCardWorkflow(
    string Reference,
    string ReasonCode,
    string Status,
    string LastOperationOutcome);

public sealed record AgentEscalationSummary(
    Guid Id,
    string Reference,
    string Reason,
    string Status,
    DateTimeOffset CreatedAt);

public enum FinancialOperationStatus { Pending, InFlight, Succeeded, Failed, Unknown }

public sealed record AgentFinancialOperation(
    string Kind, string OperationReference, FinancialOperationStatus Status, DateTimeOffset UpdatedAt);

public sealed record AgentDisputeDetails(
    Guid Id,
    string Reference,
    string CustomerReference,
    TransactionCategory Category,
    string ReasonCode,
    string CustomerStatement,
    DisputeEligibilityOutcome EligibilityOutcome,
    string EligibilityPolicyVersion,
    DisputeCaseStatus CaseStatus,
    DisputeDecisionStatus DecisionStatus,
    DisputeFinancialStatus FinancialStatus,
    DateTimeOffset CreatedAt,
    long Version,
    AgentTransactionDetails Transaction,
    string? ReversalOperationReference,
    string? BankChargeAdjustmentReference,
    AgentCardWorkflow? CardWorkflow,
    AgentEscalationSummary? ActiveEscalation,
    IReadOnlyList<AgentTimelineEvent> Timeline,
    IReadOnlyList<InvestigationNote> Notes,
    AgentAssignee? AssignedTo = null,
    IReadOnlyList<AgentFinancialOperation>? FinancialOperations = null,
    DisputeSlaAssessment? Sla = null);

public sealed record InvestigationNote(
    Guid Id,
    Guid DisputeId,
    Guid EmployeeId,
    string Text,
    DateTimeOffset CreatedAt,
    string CorrelationId);

public enum InvestigationNoteWriteOutcome
{
    Created,
    Replayed,
    IdempotencyKeyReused,
    DisputeNotFound
}

public sealed record InvestigationNoteWriteResult(
    InvestigationNoteWriteOutcome Outcome,
    InvestigationNote? Note);

public sealed record CreateInvestigationNoteCommand(
    Guid DisputeId,
    Guid EmployeeId,
    string Text,
    string IdempotencyKey,
    string CorrelationId);

public interface IAgentDisputeReader
{
    Task<AgentDisputeProcessingTarget?> ReadProcessingTargetAsync(
        Guid disputeId, CancellationToken cancellationToken);

    Task<AgentDisputePage> ReadPageAsync(ListAgentDisputesQuery query, CancellationToken cancellationToken);

    Task<AgentDisputeDetails?> ReadAsync(Guid disputeId, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(Guid disputeId, CancellationToken cancellationToken);
}

public interface IInvestigationNoteStore
{
    Task<IReadOnlyList<InvestigationNote>> ReadAsync(Guid disputeId, CancellationToken cancellationToken);

    Task<InvestigationNoteWriteResult> AddAsync(
        InvestigationNote note,
        string idempotencyKey,
        string requestFingerprint,
        CancellationToken cancellationToken);
}

public interface IListAgentDisputesHandler
{
    Task<AgentDisputePage> HandleAsync(ListAgentDisputesQuery query, CancellationToken cancellationToken);
}

public sealed class ListAgentDisputesHandler(
    IAgentDisputeReader reader, DisputeSlaPolicy sla, TimeProvider clock) : IListAgentDisputesHandler
{
    public async Task<AgentDisputePage> HandleAsync(
        ListAgentDisputesQuery query, CancellationToken cancellationToken)
    {
        var page = await reader.ReadPageAsync(query, cancellationToken);
        var now = clock.GetUtcNow();
        return page with
        {
            Items = page.Items.Select(item => item with
            {
                Sla = sla.Assess(item.CaseStatus, item.CreatedAt, now)
            }).ToArray()
        };
    }
}

public interface IGetAgentDisputeHandler
{
    Task<AgentDisputeDetails?> HandleAsync(Guid disputeId, CancellationToken cancellationToken);
}

public sealed class GetAgentDisputeHandler(
    IAgentDisputeReader reader,
    IInvestigationNoteStore notes,
    DisputeSlaPolicy sla,
    TimeProvider clock) : IGetAgentDisputeHandler
{
    public async Task<AgentDisputeDetails?> HandleAsync(Guid disputeId, CancellationToken cancellationToken)
    {
        var dispute = await reader.ReadAsync(disputeId, cancellationToken);
        if (dispute is null)
            return null;
        var noteItems = await notes.ReadAsync(disputeId, cancellationToken);
        return dispute with
        {
            Notes = noteItems,
            Sla = sla.Assess(dispute.CaseStatus, dispute.CreatedAt, clock.GetUtcNow())
        };
    }
}

public interface IGetInvestigationNotesHandler
{
    Task<IReadOnlyList<InvestigationNote>?> HandleAsync(Guid disputeId, CancellationToken cancellationToken);
}

public sealed class GetInvestigationNotesHandler(
    IAgentDisputeReader reader,
    IInvestigationNoteStore notes) : IGetInvestigationNotesHandler
{
    public async Task<IReadOnlyList<InvestigationNote>?> HandleAsync(
        Guid disputeId, CancellationToken cancellationToken)
    {
        if (!await reader.ExistsAsync(disputeId, cancellationToken))
            return null;
        return await notes.ReadAsync(disputeId, cancellationToken);
    }
}

public interface ICreateInvestigationNoteHandler
{
    Task<InvestigationNoteWriteResult> HandleAsync(
        CreateInvestigationNoteCommand command, CancellationToken cancellationToken);
}

public sealed class CreateInvestigationNoteHandler(
    IInvestigationNoteStore store,
    TimeProvider timeProvider) : ICreateInvestigationNoteHandler
{
    public const int MinimumLength = 15;
    public const int MaximumLength = 2000;

    public Task<InvestigationNoteWriteResult> HandleAsync(
        CreateInvestigationNoteCommand command, CancellationToken cancellationToken)
    {
        var text = command.Text.Trim().Normalize(NormalizationForm.FormC);
        if (text.Length is < MinimumLength or > MaximumLength)
            throw new ArgumentOutOfRangeException(nameof(command), "The normalized note length is invalid.");

        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        var note = new InvestigationNote(Guid.NewGuid(), command.DisputeId, command.EmployeeId, text,
            timeProvider.GetUtcNow(), command.CorrelationId);
        return store.AddAsync(note, command.IdempotencyKey, fingerprint, cancellationToken);
    }
}
