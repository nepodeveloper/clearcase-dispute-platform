using ClearCase.Application.Disputes;
using ClearCase.Domain.Disputes;
using ClearCase.Infrastructure.Disputes;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClearCase.Api.IntegrationTests;

public sealed class ResilientDebitOrderGatewayTests
{
    [Fact]
    public async Task MockGateway_ReconcilesConfiguredRemoteSuccessByStableOperationReference()
    {
        var gateway = new MockDebitOrderProcessingGateway(
            DebitOrderGatewayOutcome.Unknown, DebitOrderGatewayOutcome.Confirmed);

        Assert.Equal(DebitOrderGatewayOutcome.Unknown,
            await gateway.ReverseAsync("MOCK-DO-case-1", CancellationToken.None));
        Assert.Equal(DebitOrderGatewayOutcome.Confirmed,
            await gateway.GetReversalStatusAsync("MOCK-DO-case-1", CancellationToken.None));
        Assert.Equal(DebitOrderGatewayOutcome.Unknown,
            await gateway.GetReversalStatusAsync("other-operation", CancellationToken.None));
    }

    [Fact]
    public async Task Reverse_WhenDependencyTimesOut_StopsCallingAfterCircuitOpens()
    {
        var gateway = new FailingGateway();
        var resilient = new ResilientDebitOrderProcessingGateway(
            gateway, TimeProvider.System, NullLogger<ResilientDebitOrderProcessingGateway>.Instance);

        Assert.Equal(DebitOrderGatewayOutcome.Unknown,
            await resilient.ReverseAsync("stable-op", CancellationToken.None));
        Assert.Equal(DebitOrderGatewayOutcome.Unknown,
            await resilient.ReverseAsync("stable-op", CancellationToken.None));
        Assert.Equal(DebitOrderGatewayOutcome.Unknown,
            await resilient.ReverseAsync("stable-op", CancellationToken.None));
        Assert.Equal(DebitOrderGatewayOutcome.Unknown,
            await resilient.ReverseAsync("different-op", CancellationToken.None));

        Assert.Equal(3, gateway.ReverseCalls);
    }

    [Fact]
    public async Task StatusQuery_UsesBoundedRetriesAndReturnsUnknownWhenBreakerOpens()
    {
        var gateway = new FailingGateway();
        var resilient = new ResilientDebitOrderProcessingGateway(
            gateway, TimeProvider.System, NullLogger<ResilientDebitOrderProcessingGateway>.Instance);

        Assert.Equal(DebitOrderGatewayOutcome.Unknown,
            await resilient.GetReversalStatusAsync("stable-op", CancellationToken.None));
        Assert.Equal(DebitOrderGatewayOutcome.Unknown,
            await resilient.GetReversalStatusAsync("stable-op", CancellationToken.None));

        Assert.Equal(3, gateway.StatusCalls);
    }

    private sealed class FailingGateway : IDebitOrderProcessingGateway
    {
        public int ReverseCalls { get; private set; }
        public int StatusCalls { get; private set; }

        public Task<DebitOrderGatewayOutcome> ReverseAsync(string operationReference, CancellationToken cancellationToken)
        {
            ReverseCalls++;
            throw new TimeoutException("Simulated dependency timeout.");
        }

        public Task<DebitOrderGatewayOutcome> GetReversalStatusAsync(
            string operationReference, CancellationToken cancellationToken)
        {
            StatusCalls++;
            throw new TimeoutException("Simulated dependency timeout.");
        }
    }
}
