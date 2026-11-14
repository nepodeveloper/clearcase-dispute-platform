using ClearCase.Domain.Disputes;
using ClearCase.Domain.Chargebacks;
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
    string? ReversalOperationReference = null,
    Chargeback? Chargeback = null,
    string? BankChargeAdjustmentReference = null,
    long Version = 1)
{
    public static DisputeView From(Dispute dispute) =>
        new(dispute.Id, dispute.Reference, dispute.TransactionId, dispute.Category, dispute.ReasonCode,
            dispute.Description, dispute.CaseStatus, dispute.DecisionStatus, dispute.FinancialStatus,
            dispute.CreatedAt, dispute.Timeline);
}

public enum DisputeAddResult
{
    Added,
    ActiveDisputeExists,
    SubmissionKeyExists
}

/// <summary>Customer-scoped idempotency record stored atomically with the case it created.</summary>
public sealed record DisputeSubmission(string IdempotencyKey, string RequestFingerprint);

public sealed record StoredDisputeSubmission(Guid DisputeId, string RequestFingerprint);

public sealed class DisputeConcurrencyConflictException : Exception { }

/// <summary>Persistence port for dispute cases; every read is scoped to the owning customer.</summary>
public interface IDisputeStore
{
    Task<DisputeListPage> ReadPageAsync(Guid customerId, ListDisputesQuery query, CancellationToken cancellationToken);

    Task<bool> HasOpenDisputeAsync(Guid customerId, Guid transactionId, CancellationToken cancellationToken);

    /// <summary>Atomically stores the case, its timeline and any submission key; constraint conflicts are reported, not thrown.</summary>
    Task<DisputeAddResult> AddAsync(Dispute dispute, DisputeSubmission? submission,
        CancellationToken cancellationToken, OutboxMetadata? outboxMetadata = null);

    Task<StoredDisputeSubmission?> FindSubmissionAsync(
        Guid customerId, string idempotencyKey, CancellationToken cancellationToken);

    Task<DisputeView?> ReadAsync(Guid customerId, Guid disputeId, CancellationToken cancellationToken);
}
