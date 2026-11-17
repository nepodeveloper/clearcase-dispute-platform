namespace ClearCase.Application.Disputes;

/// <summary>External effects are scheduled durably and executed outside a dispute transaction.</summary>
public enum FinancialOperationKind
{
    DebitReversal,
    BankChargeAdjustment,
    CardNetworkInitiate,
    CardNetworkForwardToAcquirer,
    CardNetworkSettlement
}

public sealed record FinancialOperationRequest(
    Guid CustomerId, Guid DisputeId, FinancialOperationKind Kind,
    long? ExpectedVersion, Guid? EmployeeId, string CorrelationId);

/// <summary>
/// This port supports synchronous mock exercise and independent worker recovery.
/// Never issue the same external financial command twice after an ambiguous send.
/// </summary>
public interface IFinancialOperationCoordinator
{
    Task<Guid?> ScheduleAsync(FinancialOperationRequest request, CancellationToken cancellationToken);
    Task<DisputeView?> ExecuteAsync(FinancialOperationRequest request, CancellationToken cancellationToken);
    Task<bool> RecoverNextAsync(CancellationToken cancellationToken);
}

/// <summary>Provider command data deliberately excludes EF entities and HTTP request state.</summary>
public sealed record FinancialOperationExecution(
    Guid DisputeId, FinancialOperationKind Kind, string OperationReference);

/// <summary>One interchangeable, testable adapter per family of financial effects.</summary>
public interface IFinancialOperationStrategy
{
    bool Supports(FinancialOperationKind kind);
    bool IsAvailable { get; }
    Task<string> ExecuteAsync(FinancialOperationExecution execution, CancellationToken cancellationToken);
}


/// <summary>Immutable lease snapshot returned by a durable claim. No EF types escape Infrastructure.</summary>
public sealed record FinancialIntentClaim(
    Guid Id, Guid DisputeId, Guid CustomerId, FinancialOperationKind Kind,
    string OperationReference, long ExpectedVersion, Guid? EmployeeId,
    string CorrelationId, Guid LeaseId, bool RequiresProviderExecution, string? RecordedOutcome);

/// <summary>Atomic intent scheduling, leasing and durable result persistence.</summary>
public interface IFinancialIntentStore
{
    Task<Guid?> ScheduleAsync(FinancialOperationRequest request, CancellationToken cancellationToken);
    Task<Guid?> FindRecoverableAsync(CancellationToken cancellationToken);
    Task<FinancialIntentClaim?> ClaimAsync(Guid intentId, CancellationToken cancellationToken);
    Task<bool> RecordOutcomeAsync(FinancialIntentClaim claim, string outcome, CancellationToken cancellationToken);
    Task<bool> WasAppliedAsync(FinancialIntentClaim claim, CancellationToken cancellationToken);
    Task CompleteAsync(FinancialIntentClaim claim, CancellationToken cancellationToken);
}

/// <summary>Applies a previously persisted provider result to case state, event and audit.</summary>
public interface IRecordedFinancialOutcomeApplier
{
    Task ApplyAsync(FinancialIntentClaim claim, string outcome, CancellationToken cancellationToken);
}
