using ClearCase.Application.Disputes;
using ClearCase.Domain.Disputes;

namespace ClearCase.Infrastructure.Disputes;

/// <summary>Pure deterministic simulation, safe inside the local SQLite transaction. Never calls a bank.</summary>
public sealed class MockDebitOrderProcessingGateway(
    DebitOrderGatewayOutcome outcome = DebitOrderGatewayOutcome.Confirmed,
    DebitOrderGatewayOutcome status = DebitOrderGatewayOutcome.Unknown) : IDebitOrderProcessingGateway
{
    public Task<DebitOrderGatewayOutcome> ReverseAsync(string operationReference, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationReference);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(outcome);
    }

    public Task<DebitOrderGatewayOutcome> GetReversalStatusAsync(
        string operationReference, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationReference);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(operationReference.StartsWith("MOCK-DO-", StringComparison.Ordinal)
            ? status
            : DebitOrderGatewayOutcome.Unknown);
    }
}
