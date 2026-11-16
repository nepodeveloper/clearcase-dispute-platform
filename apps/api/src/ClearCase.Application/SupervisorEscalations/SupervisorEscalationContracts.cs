using System.Security.Cryptography;
using System.Text;
using ClearCase.Domain.Disputes;
using ClearCase.Domain.Transactions;

namespace ClearCase.Application.SupervisorEscalations;

public sealed record EscalationCursor(DateTimeOffset CreatedAt, string Reference);

public sealed record ListSupervisorEscalationsQuery(
    int PageSize,
    string? Search,
    DisputeEscalationStatus? Status,
    EscalationCursor? After);

public sealed record SupervisorEscalationItem(
    Guid Id,
    string Reference,
    Guid DisputeId,
    string DisputeReference,
    string CustomerReference,
    string Reason,
    DisputeEscalationStatus Status,
    DateTimeOffset CreatedAt);

public sealed record SupervisorEscalationPage(
    IReadOnlyList<SupervisorEscalationItem> Items,
    bool HasNextPage);

public sealed record EscalationAuditEvent(
    Guid Id,
    DisputeEscalationAction Action,
    Guid EmployeeId,
    string? PrivateNote,
    DateTimeOffset OccurredAt,
    string CorrelationId);

public sealed record SupervisorEscalationDetails(
    Guid Id,
    string Reference,
    Guid DisputeId,
    string DisputeReference,
    string CustomerReference,
    TransactionCategory Category,
    DisputeCaseStatus CaseStatus,
    DisputeDecisionStatus DecisionStatus,
    DisputeFinancialStatus FinancialStatus,
    string Reason,
    DisputeEscalationStatus Status,
    Guid CreatedByEmployeeId,
    DateTimeOffset CreatedAt,
    IReadOnlyList<EscalationAuditEvent> History);

public sealed record ActiveDisputeEscalation(
    Guid Id,
    string Reference,
    string Reason,
    DisputeEscalationStatus Status,
    DateTimeOffset CreatedAt);

public enum EscalationWriteOutcome
{
    Created,
    Applied,
    Replayed,
    IdempotencyKeyReused,
    ActiveEscalationExists,
    DisputeNotFound,
    CaseNotEligible,
    EscalationNotFound,
    InvalidTransition,
    ReviewerMustBeIndependent,
    VersionConflict
}

public sealed record EscalationWriteResult(
    EscalationWriteOutcome Outcome,
    SupervisorEscalationDetails? Escalation);

public sealed record CreateDisputeEscalationCommand(
    Guid DisputeId,
    Guid EmployeeId,
    string Reason,
    string Note,
    string IdempotencyKey,
    string CorrelationId);

public sealed record AcknowledgeEscalationCommand(
    Guid EscalationId,
    Guid EmployeeId,
    string IdempotencyKey,
    string CorrelationId);

public sealed record ResolveEscalationCommand(
    Guid EscalationId,
    Guid EmployeeId,
    string Note,
    string IdempotencyKey,
    string CorrelationId);

public interface ISupervisorEscalationStore
{
    Task<SupervisorEscalationPage> ReadPageAsync(
        ListSupervisorEscalationsQuery query, CancellationToken cancellationToken);

    Task<SupervisorEscalationDetails?> ReadAsync(Guid escalationId, CancellationToken cancellationToken);

    Task<ActiveDisputeEscalation?> ReadActiveForDisputeAsync(
        Guid disputeId, CancellationToken cancellationToken);

    Task<EscalationWriteResult> CreateAsync(
        CreateDisputeEscalationCommand command,
        string requestFingerprint,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken);

    Task<EscalationWriteResult> AcknowledgeAsync(
        AcknowledgeEscalationCommand command,
        string requestFingerprint,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken);

    Task<EscalationWriteResult> ResolveAsync(
        ResolveEscalationCommand command,
        string requestFingerprint,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken);
}

public sealed class ListSupervisorEscalationsHandler(ISupervisorEscalationStore store)
{
    public Task<SupervisorEscalationPage> HandleAsync(
        ListSupervisorEscalationsQuery query, CancellationToken cancellationToken) =>
        store.ReadPageAsync(query, cancellationToken);
}

public sealed class GetSupervisorEscalationHandler(ISupervisorEscalationStore store)
{
    public Task<SupervisorEscalationDetails?> HandleAsync(
        Guid id, CancellationToken cancellationToken) => store.ReadAsync(id, cancellationToken);
}

public sealed class GetActiveDisputeEscalationHandler(ISupervisorEscalationStore store)
{
    public Task<ActiveDisputeEscalation?> HandleAsync(
        Guid id, CancellationToken cancellationToken) =>
        store.ReadActiveForDisputeAsync(id, cancellationToken);
}

public sealed class CreateDisputeEscalationHandler(
    ISupervisorEscalationStore store,
    TimeProvider timeProvider)
{
    public Task<EscalationWriteResult> HandleAsync(
        CreateDisputeEscalationCommand command, CancellationToken cancellationToken)
    {
        var note = NormalizeNote(command.Note);
        var fingerprint = Fingerprint($"{command.Reason}\n{note}");
        return store.CreateAsync(command with { Note = note }, fingerprint,
            timeProvider.GetUtcNow(), cancellationToken);
    }

    internal static string NormalizeNote(string value) => value.Trim().Normalize(NormalizationForm.FormC);

    internal static string Fingerprint(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

public sealed class AcknowledgeEscalationHandler(
    ISupervisorEscalationStore store,
    TimeProvider timeProvider)
{
    public Task<EscalationWriteResult> HandleAsync(
        AcknowledgeEscalationCommand command, CancellationToken cancellationToken) =>
        store.AcknowledgeAsync(command, CreateDisputeEscalationHandler.Fingerprint("acknowledge"),
            timeProvider.GetUtcNow(), cancellationToken);
}

public sealed class ResolveEscalationHandler(
    ISupervisorEscalationStore store,
    TimeProvider timeProvider)
{
    public Task<EscalationWriteResult> HandleAsync(
        ResolveEscalationCommand command, CancellationToken cancellationToken)
    {
        var note = CreateDisputeEscalationHandler.NormalizeNote(command.Note);
        return store.ResolveAsync(command with { Note = note },
            CreateDisputeEscalationHandler.Fingerprint(note), timeProvider.GetUtcNow(), cancellationToken);
    }
}
