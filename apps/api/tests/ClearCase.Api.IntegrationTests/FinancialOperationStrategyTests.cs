using ClearCase.Application.Chargebacks;
using ClearCase.Application.Disputes;
using ClearCase.Domain.Chargebacks;
using ClearCase.Domain.Disputes;
using ClearCase.Infrastructure.Disputes;

namespace ClearCase.Api.IntegrationTests;

public sealed class FinancialOperationStrategyTests
{
    [Theory]
    [InlineData(FinancialOperationKind.CardNetworkInitiate, ChargebackCommand.Initiate)]
    [InlineData(FinancialOperationKind.CardNetworkForwardToAcquirer, ChargebackCommand.ForwardToAcquirer)]
    [InlineData(FinancialOperationKind.CardNetworkSettlement, ChargebackCommand.ExecuteSettlement)]
    public async Task CardStrategy_ForwardsStableReferenceAndCorrectCommand(
        FinancialOperationKind kind, ChargebackCommand expected)
    {
        var gateway = new RecordingCardGateway();
        var strategy = new CardNetworkStrategy(gateway, null!, TimeProvider.System);
        var id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var operation = new FinancialOperationExecution(id, kind, "CARD-OP-123");

        var result = await strategy.ExecuteAsync(operation, CancellationToken.None);

        Assert.Equal(CardNetworkOutcome.Succeeded.ToString(), result);
        Assert.Equal(expected, gateway.LastRequest?.Command);
        Assert.Equal("CARD-OP-123", gateway.LastRequest?.OperationReference);
        Assert.Equal($"MOCK-CB-{id:N}", gateway.LastRequest?.ChargebackReference);
        Assert.Equal(1, gateway.Calls);
    }

    [Fact]
    public async Task DebitStrategy_DoesNotNeedDatabaseOrProviderNetwork()
    {
        var gateway = new RecordingDebitGateway();
        var strategy = new DebitReversalStrategy(gateway, null!, TimeProvider.System);
        var result = await strategy.ExecuteAsync(
            new FinancialOperationExecution(Guid.NewGuid(), FinancialOperationKind.DebitReversal, "OP-1"),
            CancellationToken.None);

        Assert.Equal(DebitOrderGatewayOutcome.Confirmed.ToString(), result);
        Assert.Equal("OP-1", gateway.Reference);
        Assert.Equal(1, gateway.Calls);
    }

    [Fact]
    public void UnavailableGateways_FailClosedBeforeIntentScheduling()
    {
        var unavailable = new UnavailableDisputeProcessingGateway();
        Assert.False(new DebitReversalStrategy(unavailable, null!, TimeProvider.System).IsAvailable);
        Assert.False(new BankChargeAdjustmentStrategy(unavailable, null!, TimeProvider.System).IsAvailable);
        Assert.False(new CardNetworkStrategy(unavailable, null!, TimeProvider.System).IsAvailable);
    }

    private sealed class RecordingCardGateway : ICardNetworkGateway
    {
        public CardNetworkRequest? LastRequest { get; private set; }
        public int Calls { get; private set; }
        public Task<CardNetworkOutcome> ExecuteAsync(CardNetworkRequest request, CancellationToken ct)
        {
            Calls++;
            LastRequest = request;
            return Task.FromResult(CardNetworkOutcome.Succeeded);
        }
    }

    private sealed class RecordingDebitGateway : IDebitOrderProcessingGateway
    {
        public int Calls { get; private set; }
        public string? Reference { get; private set; }
        public Task<DebitOrderGatewayOutcome> ReverseAsync(string operationReference, CancellationToken ct)
        {
            Calls++;
            Reference = operationReference;
            return Task.FromResult(DebitOrderGatewayOutcome.Confirmed);
        }
        public Task<DebitOrderGatewayOutcome> GetReversalStatusAsync(string operationReference, CancellationToken ct) =>
            Task.FromResult(DebitOrderGatewayOutcome.Unknown);
    }
}
