using ClearCase.Application.AgentDisputes;
using ClearCase.Application.Chargebacks;
using ClearCase.Application.Disputes;
using ClearCase.Domain.Chargebacks;
using ClearCase.Domain.Disputes;

namespace ClearCase.Infrastructure.Disputes;

public sealed class UnavailableDisputeProcessingGateway :
    IDebitOrderProcessingGateway, IBankChargeAdjustmentGateway, ICardNetworkGateway
{
    public Task<DebitOrderGatewayOutcome> ReverseAsync(string operationReference, CancellationToken cancellationToken) =>
        throw new DisputeProcessingUnavailableException();

    public Task<DebitOrderGatewayOutcome> GetReversalStatusAsync(string operationReference, CancellationToken cancellationToken) =>
        throw new DisputeProcessingUnavailableException();

    public Task<BankChargeAdjustmentOutcome> AdjustAsync(string operationReference, CancellationToken cancellationToken) =>
        throw new DisputeProcessingUnavailableException();

    public Task<CardNetworkOutcome> ExecuteAsync(CardNetworkRequest request, CancellationToken cancellationToken) =>
        throw new DisputeProcessingUnavailableException();
}
