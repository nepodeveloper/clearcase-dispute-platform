namespace ClearCase.Application.AgentDisputes;

public enum DisputeAssignmentOutcome
{
    NotFound,
    Claimed,
    AlreadyMine,
    AlreadyAssignedToOther,
    Released,
    NotAssigned,
    Forbidden
}

/// <summary>Persists workload assignment and its append-only audit; it never touches the case version.</summary>
public interface IDisputeAssignmentStore
{
    Task<DisputeAssignmentOutcome> ClaimAsync(
        Guid disputeId, Guid employeeId, string correlationId, DateTimeOffset occurredAt,
        CancellationToken cancellationToken);

    Task<DisputeAssignmentOutcome> ReleaseAsync(
        Guid disputeId, Guid actorEmployeeId, bool actorIsSupervisor, string correlationId,
        DateTimeOffset occurredAt, CancellationToken cancellationToken);
}

public sealed record DisputeAssignmentResult(DisputeAssignmentOutcome Outcome, AgentDisputeDetails? Dispute);

public interface IDisputeAssignmentHandler
{
    Task<DisputeAssignmentResult> ClaimAsync(
        Guid disputeId, Guid employeeId, string correlationId, CancellationToken cancellationToken);

    Task<DisputeAssignmentResult> ReleaseAsync(
        Guid disputeId, Guid actorEmployeeId, bool actorIsSupervisor, string correlationId,
        CancellationToken cancellationToken);
}

public sealed class DisputeAssignmentHandler(
    IDisputeAssignmentStore store, IGetAgentDisputeHandler details, TimeProvider clock)
    : IDisputeAssignmentHandler
{
    public async Task<DisputeAssignmentResult> ClaimAsync(
        Guid disputeId, Guid employeeId, string correlationId, CancellationToken cancellationToken) =>
        await WithDetailsAsync(disputeId, await store.ClaimAsync(
            disputeId, employeeId, correlationId, clock.GetUtcNow(), cancellationToken), cancellationToken);

    public async Task<DisputeAssignmentResult> ReleaseAsync(
        Guid disputeId, Guid actorEmployeeId, bool actorIsSupervisor, string correlationId,
        CancellationToken cancellationToken) =>
        await WithDetailsAsync(disputeId, await store.ReleaseAsync(
            disputeId, actorEmployeeId, actorIsSupervisor, correlationId, clock.GetUtcNow(), cancellationToken),
            cancellationToken);

    private async Task<DisputeAssignmentResult> WithDetailsAsync(
        Guid disputeId, DisputeAssignmentOutcome outcome, CancellationToken cancellationToken) =>
        outcome is DisputeAssignmentOutcome.Claimed or DisputeAssignmentOutcome.AlreadyMine
            or DisputeAssignmentOutcome.Released or DisputeAssignmentOutcome.NotAssigned
            ? new(outcome, await details.HandleAsync(disputeId, cancellationToken))
            : new(outcome, null);
}
