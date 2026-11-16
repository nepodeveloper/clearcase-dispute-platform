using System.Security.Cryptography;
using System.Text;
using ClearCase.Domain.DisputeEligibility;
using ClearCase.Domain.Disputes;
using ClearCase.Domain.Transactions;

namespace ClearCase.Application.AgentDisputes;

public sealed record AgentDisputeCursor(DateTimeOffset CreatedAt, string Reference);

public sealed record ListAgentDisputesQuery(
    int PageSize,
    string? Search,
    TransactionCategory? Category,
    DisputeCaseStatus? CaseStatus,
    AgentDisputeCursor? After);

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
    long Version);

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
    IReadOnlyList<InvestigationNote> Notes);

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

public sealed class ListAgentDisputesHandler(IAgentDisputeReader reader) : IListAgentDisputesHandler
{
    public Task<AgentDisputePage> HandleAsync(
        ListAgentDisputesQuery query, CancellationToken cancellationToken) =>
        reader.ReadPageAsync(query, cancellationToken);
}

public interface IGetAgentDisputeHandler
{
    Task<AgentDisputeDetails?> HandleAsync(Guid disputeId, CancellationToken cancellationToken);
}

public sealed class GetAgentDisputeHandler(
    IAgentDisputeReader reader,
    IInvestigationNoteStore notes) : IGetAgentDisputeHandler
{
    public async Task<AgentDisputeDetails?> HandleAsync(Guid disputeId, CancellationToken cancellationToken)
    {
        var dispute = await reader.ReadAsync(disputeId, cancellationToken);
        if (dispute is null)
            return null;
        var noteItems = await notes.ReadAsync(disputeId, cancellationToken);
        return dispute with { Notes = noteItems };
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
