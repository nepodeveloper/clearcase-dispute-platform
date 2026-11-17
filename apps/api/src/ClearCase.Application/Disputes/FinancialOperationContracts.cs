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
