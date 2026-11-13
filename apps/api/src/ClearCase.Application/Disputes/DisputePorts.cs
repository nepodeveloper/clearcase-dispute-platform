using ClearCase.Domain.Disputes;
using ClearCase.Domain.Transactions;

namespace ClearCase.Application.Disputes;

public sealed record DisputeView(
    Guid Id,
    string Reference,
    Guid TransactionId,
    TransactionCategory Category,
    string ReasonCode,
    string Description,
    DisputeCaseStatus CaseStatus,
    DisputeDecisionStatus DecisionStatus,
    DisputeFinancialStatus FinancialStatus,
    DateTimeOffset CreatedAt,
    IReadOnlyList<DisputeEvent> Timeline,
    string? ReversalOperationReference = null)
{
    public static DisputeView From(Dispute dispute) =>
        new(dispute.Id, dispute.Reference, dispute.TransactionId, dispute.Category, dispute.ReasonCode,
            dispute.Description, dispute.CaseStatus, dispute.DecisionStatus, dispute.FinancialStatus,
            dispute.CreatedAt, dispute.Timeline);
}

public enum DisputeAddResult
{
    Added,
    ActiveDisputeExists
}

/// <summary>Persistence port for dispute cases; every read is scoped to the owning customer.</summary>
public interface IDisputeStore
{
    Task<bool> HasOpenDisputeAsync(Guid customerId, Guid transactionId, CancellationToken cancellationToken);

    /// <summary>Atomically stores the case and its timeline; a concurrent open case is reported, not thrown.</summary>
    Task<DisputeAddResult> AddAsync(Dispute dispute, CancellationToken cancellationToken);

    Task<DisputeView?> ReadAsync(Guid customerId, Guid disputeId, CancellationToken cancellationToken);
}
