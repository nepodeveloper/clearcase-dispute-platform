using ClearCase.Application.Transactions;
using ClearCase.Domain.Transactions;

namespace ClearCase.Api.IntegrationTests;

public sealed class TransactionApplicationTests
{
    [Fact]
    public async Task HandleAsync_UsesCustomerContextAndForwardsTheQuery()
    {
        var customerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var query = new ListTransactionsQuery(
            2, 5, TransactionType.DebitOrder, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 12));
        var expected = new TransactionListResponse([], 2, 5, 0, 0, false);
        var reader = new RecordingTransactionReader(expected);
        var handler = new ListTransactionsHandler(new FixedCustomerContext(customerId), reader);

        var result = await handler.HandleAsync(query, CancellationToken.None);

        Assert.Equal(expected, result);
        Assert.Equal(customerId, reader.CustomerId);
        Assert.Equal(query, reader.Query);
    }

    [Fact]
    public async Task GetDetails_UsesCustomerContextAndPropagatesCancellation()
    {
        var customerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var transactionId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var expected = new TransactionDetails(
            transactionId, "CardPurchase", "Woolworths", "Woolworths", 750.00m, "ZAR",
            new DateTimeOffset(2026, 11, 10, 8, 30, 0, TimeSpan.Zero), "Posted", "TXN-2026-000001", true);
        var reader = new RecordingTransactionReader(new TransactionListResponse([], 1, 20, 0, 0, false), expected);
        var handler = new GetTransactionDetailsHandler(new FixedCustomerContext(customerId), reader);
        using var cancellation = new CancellationTokenSource();

        var result = await handler.HandleAsync(transactionId, cancellation.Token);

        Assert.Equal(expected, result);
        Assert.Equal(customerId, reader.CustomerId);
        Assert.Equal(transactionId, reader.TransactionId);
        Assert.Equal(cancellation.Token, reader.CancellationToken);
    }

    private sealed class FixedCustomerContext(Guid customerId) : ICustomerContext
    {
        public Guid CustomerId { get; } = customerId;
    }

    private sealed class RecordingTransactionReader(
        TransactionListResponse result,
        TransactionDetails? details = null) : ITransactionReader
    {
        public Guid CustomerId { get; private set; }

        public ListTransactionsQuery? Query { get; private set; }

        public Guid TransactionId { get; private set; }

        public CancellationToken CancellationToken { get; private set; }

        public Task<TransactionListResponse> ReadAsync(
            Guid customerId,
            ListTransactionsQuery query,
            CancellationToken cancellationToken)
        {
            CustomerId = customerId;
            Query = query;
            return Task.FromResult(result);
        }

        public Task<TransactionDetails?> ReadByIdAsync(
            Guid customerId,
            Guid transactionId,
            CancellationToken cancellationToken)
        {
            CustomerId = customerId;
            TransactionId = transactionId;
            CancellationToken = cancellationToken;
            return Task.FromResult(details);
        }

        public Task<DisputeTransaction?> ReadForEligibilityAsync(
            Guid customerId,
            Guid transactionId,
            CancellationToken cancellationToken)
        {
            CustomerId = customerId;
            TransactionId = transactionId;
            CancellationToken = cancellationToken;
            return Task.FromResult<DisputeTransaction?>(null);
        }
    }
}
