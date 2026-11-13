using ClearCase.Application.DisputeEligibility;
using ClearCase.Domain.DisputeEligibility;
using ClearCase.Domain.Transactions;
using ClearCase.Infrastructure.Transactions;

namespace ClearCase.Api.IntegrationTests;

public sealed class DebitOrderMandateReaderTests
{
    private static readonly Guid CustomerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TransactionId = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Instant = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task MockReader_ReturnsOnlyKnownCustomerScopedSnapshots()
    {
        var reader = new MockDebitOrderMandateReader();
        var first = await reader.ReadAsync(CustomerId, TransactionId, CancellationToken.None);
        var repeat = await reader.ReadAsync(CustomerId, TransactionId, CancellationToken.None);

        Assert.NotNull(first);
        Assert.Equal(first, repeat);
        Assert.Null(await reader.ReadAsync(
            Guid.Parse("22222222-2222-2222-2222-222222222222"), TransactionId, CancellationToken.None));
        Assert.Null(await reader.ReadAsync(
            CustomerId, Guid.Parse("00000000-0000-0000-0000-000000000008"), CancellationToken.None));
        Assert.Null(await reader.ReadAsync(
            CustomerId, Guid.Parse("00000000-0000-0000-0000-000000000100"), CancellationToken.None));
    }

    [Fact]
    public async Task Evaluator_PassesCustomerAndCancellationAndDoesNotFallBackToEmbeddedMandate()
    {
        var reader = new RecordingReader();
        var evaluator = Evaluator(reader);
        using var cancellation = new CancellationTokenSource();

        var result = await evaluator.EvaluateAsync(CustomerId, Transaction(), cancellation.Token);

        Assert.Equal(CustomerId, reader.CustomerId);
        Assert.Equal(TransactionId, reader.TransactionId);
        Assert.Equal(cancellation.Token, reader.CancellationToken);
        Assert.Equal(DisputeEligibilityOutcome.RequiresManualReview, result.Outcome);
        Assert.Contains("debitOrder.mandateInformationMissing", result.ReasonCodes);
        Assert.Equal(DisputeReviewRoute.Investigation, result.ReviewRoute);
    }

    [Fact]
    public async Task Evaluator_DoesNotReadMandatesForCards()
    {
        var reader = new RecordingReader { Failure = new InvalidOperationException("Must not be called.") };

        await Evaluator(reader).EvaluateAsync(CustomerId,
            Transaction() with { Category = TransactionCategory.CardPurchase }, CancellationToken.None);

        Assert.Equal(0, reader.Calls);
    }

    [Fact]
    public async Task Evaluator_PropagatesReaderFailureRatherThanReturningAFalseDecision()
    {
        var failure = new InvalidOperationException("Synthetic reader failure.");
        var reader = new RecordingReader { Failure = failure };

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Evaluator(reader).EvaluateAsync(CustomerId, Transaction(), CancellationToken.None));

        Assert.Same(failure, actual);
    }

    private static DisputeEligibilityEvaluator Evaluator(IDebitOrderMandateReader reader) =>
        new([new DebitOrderEligibilityPolicy(), new CardPurchaseEligibilityPolicy()],
            new DisputeEligibilityPolicySettings
            {
                DemoPoliciesEnabled = true,
                DebitOrder = [new()
                {
                    EffectiveFrom = new(2026, 4, 13), WindowCalendarDays = 60, StandardReviewAmountLimit = 2000m
                }]
            }, new FrozenTimeProvider(), reader);

    private static DisputeTransaction Transaction() =>
        new(TransactionId, TransactionCategory.DebitOrder, 420.50m, "ZAR", Instant.AddDays(-1),
            TransactionStatus.Posted, true, true, new(2026, 11, 11),
            DebitOrderMandateType.EftDebitOrder, true, true,
            new(DebitOrderMandateType.EftDebitOrder, DebitOrderMandateStatus.Active, true,
                420.50m, "ZAR", new(2026, 11, 11)));

    private sealed class FrozenTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Instant;
    }

    private sealed class RecordingReader : IDebitOrderMandateReader
    {
        public Guid CustomerId { get; private set; }
        public Guid TransactionId { get; private set; }
        public CancellationToken CancellationToken { get; private set; }
        public int Calls { get; private set; }
        public Exception? Failure { get; init; }

        public Task<DebitOrderMandate?> ReadAsync(
            Guid customerId, Guid transactionId, CancellationToken cancellationToken)
        {
            Calls++;
            CustomerId = customerId;
            TransactionId = transactionId;
            CancellationToken = cancellationToken;
            return Failure is null ? Task.FromResult<DebitOrderMandate?>(null)
                : Task.FromException<DebitOrderMandate?>(Failure);
        }
    }
}
