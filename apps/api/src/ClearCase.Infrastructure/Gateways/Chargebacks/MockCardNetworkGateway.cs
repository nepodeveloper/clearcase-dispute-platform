using ClearCase.Application.Chargebacks;
using ClearCase.Domain.Chargebacks;

namespace ClearCase.Infrastructure.Chargebacks;

/// <summary>Pure local simulation; unknown models a timeout, never an assumed network failure.</summary>
public sealed class MockCardNetworkGateway(CardNetworkOutcome outcome = CardNetworkOutcome.Succeeded) : ICardNetworkGateway
{
    public Task<CardNetworkOutcome> ExecuteAsync(CardNetworkRequest request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OperationReference);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ChargebackReference);
        if (!Chargeback.UsesGateway(request.Command))
            throw new ArgumentException("This command is not a card-network operation.", nameof(request));
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(outcome);
    }
}
