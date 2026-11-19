using ClearCase.Domain.DisputeEligibility;
using ClearCase.Domain.Transactions;

namespace ClearCase.Api.IntegrationTests;

public sealed class DisputeEligibilityPolicyTests
{
    private static readonly DateTimeOffset EvaluatedAt =
        new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CardPurchase_PostedDebitWithinConfiguredWindow_IsEligible()
    {
        var result = new CardPurchaseEligibilityPolicy().Evaluate(
            Transaction(TransactionCategory.CardPurchase), Context());

        Assert.Equal(DisputeEligibilityOutcome.EligibleToSubmit, result.Outcome);
        Assert.Empty(result.ReasonCodes);
        Assert.Equal("demo-v1", result.PolicyVersion);
        Assert.Equal(EvaluatedAt, result.EvaluatedAt);
    }

    [Fact]
    public void CardPurchase_PendingAuthorization_IsIneligible()
    {
        var result = new CardPurchaseEligibilityPolicy().Evaluate(
            Transaction(TransactionCategory.CardPurchase, status: TransactionStatus.PendingAuthorization),
            Context());

        Assert.Equal(DisputeEligibilityOutcome.Ineligible, result.Outcome);
        Assert.Contains("card.pendingAuthorization", result.ReasonCodes);
    }

    [Fact]
    public void CardPurchase_OutsideConfiguredDemoWindow_IsIneligible()
    {
        var result = new CardPurchaseEligibilityPolicy().Evaluate(
            Transaction(TransactionCategory.CardPurchase, transactionDate: EvaluatedAt.AddDays(-121)),
            Context());

        Assert.Equal(DisputeEligibilityOutcome.Ineligible, result.Outcome);
        Assert.Contains("card.outsideDemoWindow", result.ReasonCodes);
    }

    [Fact]
    public void CardPurchase_OnConfiguredWindowBoundary_IsEligible()
    {
        var clock = new FrozenTimeProvider(EvaluatedAt);
        var result = new CardPurchaseEligibilityPolicy().Evaluate(
            Transaction(TransactionCategory.CardPurchase, transactionDate: clock.GetUtcNow().AddDays(-120)),
            Context(clock: clock));

        Assert.Equal(DisputeEligibilityOutcome.EligibleToSubmit, result.Outcome);
    }

    [Fact]
    public void CardPurchase_InvalidAmount_RequiresManualReview()
    {
        var result = new CardPurchaseEligibilityPolicy().Evaluate(
            Transaction(TransactionCategory.CardPurchase, amount: 0), Context());

        Assert.Equal(DisputeEligibilityOutcome.RequiresManualReview, result.Outcome);
        Assert.Contains("card.invalidAmount", result.ReasonCodes);
    }

    [Fact]
    public void CardPurchase_MissingRequiredInformation_RequiresManualReview()
    {
        var result = new CardPurchaseEligibilityPolicy().Evaluate(
            Transaction(TransactionCategory.CardPurchase, hasRequiredInformation: false),
            Context());

        Assert.Equal(DisputeEligibilityOutcome.RequiresManualReview, result.Outcome);
        Assert.Contains("transaction.informationMissing", result.ReasonCodes);
    }

    [Fact]
    public void CardPurchase_NonDebitTransaction_IsIneligible()
    {
        var result = new CardPurchaseEligibilityPolicy().Evaluate(
            Transaction(TransactionCategory.CardPurchase, isDebit: false), Context());

        Assert.Equal(DisputeEligibilityOutcome.Ineligible, result.Outcome);
        Assert.Contains("transaction.notDebit", result.ReasonCodes);
    }

    [Fact]
    public void CardPurchase_DisabledDemoWindow_RequiresManualReview()
    {
        var result = new CardPurchaseEligibilityPolicy().Evaluate(
            Transaction(TransactionCategory.CardPurchase),
            Context(settings: new DisputeEligibilityPolicySettings
            {
                DemoPoliciesEnabled = true,
                Card = new CardEligibilitySettings { DemoWindowEnabled = false }
            }));

        Assert.Equal(DisputeEligibilityOutcome.RequiresManualReview, result.Outcome);
        Assert.Contains("card.windowNotConfigured", result.ReasonCodes);
    }

    [Fact]
    public void DebitOrder_EftWithinWindowAndBelowThreshold_IsEligible()
    {
        var result = new DebitOrderEligibilityPolicy().Evaluate(
            Transaction(TransactionCategory.DebitOrder, collectionDate: new DateOnly(2026, 11, 11)),
            Context());

        Assert.Equal(DisputeEligibilityOutcome.EligibleToSubmit, result.Outcome);
    }

    [Fact]
    public void DebitOrder_RegisteredMandateWithinWindow_IsEligible()
    {
        var result = new DebitOrderEligibilityPolicy().Evaluate(
            Transaction(TransactionCategory.DebitOrder, mandateType: DebitOrderMandateType.RegisteredMandate),
            Context());

        Assert.Equal(DisputeEligibilityOutcome.EligibleToSubmit, result.Outcome);
    }

    [Theory]
    [InlineData(-1, DisputeEligibilityOutcome.RequiresManualReview)]
    [InlineData(0, DisputeEligibilityOutcome.RequiresManualReview)]
    [InlineData(1, DisputeEligibilityOutcome.EligibleToSubmit)]
    [InlineData(60, DisputeEligibilityOutcome.EligibleToSubmit)]
    [InlineData(61, DisputeEligibilityOutcome.RequiresManualReview)]
    public void DebitOrder_AppliesInclusiveConfiguredWindowBoundaries(
        int daysAfterCollection,
        DisputeEligibilityOutcome expected)
    {
        var clock = new FrozenTimeProvider(EvaluatedAt);
        var collectionDate = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime).AddDays(-daysAfterCollection);
        var result = new DebitOrderEligibilityPolicy().Evaluate(
            Transaction(TransactionCategory.DebitOrder, collectionDate: collectionDate),
            Context(clock: clock));

        Assert.Equal(expected, result.Outcome);
        if (expected == DisputeEligibilityOutcome.RequiresManualReview)
            Assert.Contains("debitOrder.outsideStandardWindow", result.ReasonCodes);
    }

    [Fact]
    public void DebitOrder_ValidAuthorisedDebiCheck_RequiresManualReview()
    {
        var result = new DebitOrderEligibilityPolicy().Evaluate(
            Transaction(TransactionCategory.DebitOrder, mandateType: DebitOrderMandateType.DebiCheck),
            Context());

        Assert.Equal(DisputeEligibilityOutcome.RequiresManualReview, result.Outcome);
        Assert.Contains("debitOrder.authorisedDebiCheckRequiresReview", result.ReasonCodes);
        Assert.Contains("not automatically reversible", result.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(DisputeEligibilityOutcome.Ineligible, result.OrdinaryReversalOutcome);
        Assert.Equal(DisputeReviewRoute.Investigation, result.ReviewRoute);
    }

    [Theory]
    [InlineData(DebitOrderMandateType.EftDebitOrder)]
    [InlineData(DebitOrderMandateType.RegisteredMandate)]
    [InlineData(DebitOrderMandateType.DebiCheck)]
    public void DebitOrder_UnauthorisedCollection_RequiresManualReview(DebitOrderMandateType type)
    {
        var result = new DebitOrderEligibilityPolicy().Evaluate(
            Transaction(TransactionCategory.DebitOrder, mandateType: type, mandateAuthorised: false),
            Context());

        Assert.Equal(DisputeEligibilityOutcome.RequiresManualReview, result.Outcome);
        Assert.Contains("debitOrder.unauthorisedCollection", result.ReasonCodes);
        Assert.Equal(DisputeReviewRoute.Investigation, result.ReviewRoute);
        Assert.Null(result.OrdinaryReversalOutcome);
    }

    [Fact]
    public void DebitOrder_MissingMandateInformation_RequiresManualReview()
    {
        var result = new DebitOrderEligibilityPolicy().Evaluate(
            Transaction(TransactionCategory.DebitOrder, mandateType: null, mandateAuthorised: null),
            Context());

        Assert.Equal(DisputeEligibilityOutcome.RequiresManualReview, result.Outcome);
        Assert.Contains("debitOrder.mandateInformationMissing", result.ReasonCodes);
    }

    [Theory]
    [InlineData(1999.99, DisputeEligibilityOutcome.EligibleToSubmit)]
    [InlineData(2000.00, DisputeEligibilityOutcome.EligibleToSubmit)]
    [InlineData(2000.01, DisputeEligibilityOutcome.RequiresManualReview)]
    public void DebitOrder_UsesConfiguredReviewThreshold(decimal amount, DisputeEligibilityOutcome expected)
    {
        var result = new DebitOrderEligibilityPolicy().Evaluate(
            Transaction(TransactionCategory.DebitOrder, amount: amount), Context());

        Assert.Equal(expected, result.Outcome);
        if (amount > 2000m)
            Assert.Contains("debitOrder.amountAboveStandardReviewThreshold", result.ReasonCodes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void DebitOrder_NonPositiveAmount_RequiresManualReview(decimal amount)
    {
        var result = new DebitOrderEligibilityPolicy().Evaluate(
            Transaction(TransactionCategory.DebitOrder, amount: amount), Context());

        Assert.Equal(DisputeEligibilityOutcome.RequiresManualReview, result.Outcome);
        Assert.Contains("debitOrder.invalidAmount", result.ReasonCodes);
    }

    [Fact]
    public void DebitOrder_MissingCollectionInformation_RequiresManualReview()
    {
        var result = new DebitOrderEligibilityPolicy().Evaluate(
            Transaction(TransactionCategory.DebitOrder, hasRequiredInformation: false),
            Context());

        Assert.Equal(DisputeEligibilityOutcome.RequiresManualReview, result.Outcome);
        Assert.Contains("debitOrder.collectionInformationMissing", result.ReasonCodes);
    }

    [Fact]
    public void DebitOrder_CollectionBeforeEffectivePolicy_RequiresManualReview()
    {
        var result = new DebitOrderEligibilityPolicy().Evaluate(
            Transaction(TransactionCategory.DebitOrder, collectionDate: new DateOnly(2026, 4, 12)),
            Context());

        Assert.Equal(DisputeEligibilityOutcome.RequiresManualReview, result.Outcome);
        Assert.Contains("debitOrder.policyUnavailable", result.ReasonCodes);
    }

    [Fact]
    public void DebitOrder_SelectsPolicyEffectiveOnCollectionDate()
    {
        var settings = new DisputeEligibilityPolicySettings
        {
            PolicyVersion = "demo-v2",
            DemoPoliciesEnabled = true,
            DebitOrder =
            [
                new DebitOrderEligibilitySettings
                {
                    EffectiveFrom = new DateOnly(2026, 4, 13),
                    EffectiveTo = new DateOnly(2026, 10, 31),
                    WindowCalendarDays = 30,
                    StandardReviewAmountLimit = 2000m
                },
                new DebitOrderEligibilitySettings
                {
                    EffectiveFrom = new DateOnly(2026, 11, 1),
                    WindowCalendarDays = 60,
                    StandardReviewAmountLimit = 2000m
                }
            ]
        };
        var result = new DebitOrderEligibilityPolicy().Evaluate(
            Transaction(TransactionCategory.DebitOrder, collectionDate: new DateOnly(2026, 10, 10)),
            Context(settings));

        Assert.Equal(DisputeEligibilityOutcome.RequiresManualReview, result.Outcome);
        Assert.Contains("debitOrder.outsideStandardWindow", result.ReasonCodes);
        Assert.Equal("demo-v2", result.PolicyVersion);
    }

    [Fact]
    public void DebitOrder_AbsentReaderSnapshot_DoesNotTrustLegacyTransactionMandateFields()
    {
        var transaction = Transaction(TransactionCategory.DebitOrder) with { VerifiedMandate = null };

        var result = new DebitOrderEligibilityPolicy().Evaluate(transaction, Context());

        Assert.Equal(DisputeEligibilityOutcome.RequiresManualReview, result.Outcome);
        Assert.Contains("debitOrder.mandateInformationMissing", result.ReasonCodes);
        Assert.Equal(DisputeReviewRoute.Investigation, result.ReviewRoute);
    }

    [Theory]
    [InlineData("status", "debitOrder.mandateInformationMissing")]
    [InlineData("amount", "debitOrder.mandateInformationMissing")]
    [InlineData("currency", "debitOrder.mandateInformationMissing")]
    [InlineData("date", "debitOrder.mandateInformationMissing")]
    [InlineData("cancelled", "debitOrder.mandateNotActive")]
    [InlineData("suspended", "debitOrder.mandateNotActive")]
    [InlineData("amountMismatch", "debitOrder.mandateMismatch")]
    [InlineData("currencyMismatch", "debitOrder.mandateMismatch")]
    [InlineData("dateMismatch", "debitOrder.mandateMismatch")]
    [InlineData("unknownType", "debitOrder.mandateInformationMissing")]
    public void DebitOrder_UnverifiedDebiCheck_IsInvestigatedRatherThanTreatedAsValid(
        string scenario, string reason)
    {
        var transaction = Transaction(TransactionCategory.DebitOrder, mandateType: DebitOrderMandateType.DebiCheck);
        var mandate = Assert.IsType<DebitOrderMandate>(transaction.VerifiedMandate);
        mandate = scenario switch
        {
            "status" => mandate with { StatusAtCollection = null },
            "amount" => mandate with { AuthorisedAmount = null },
            "currency" => mandate with { Currency = null },
            "date" => mandate with { CollectionDate = null },
            "cancelled" => mandate with { StatusAtCollection = DebitOrderMandateStatus.Cancelled },
            "suspended" => mandate with { StatusAtCollection = DebitOrderMandateStatus.Suspended },
            "amountMismatch" => mandate with { AuthorisedAmount = transaction.Amount + 0.01m },
            "currencyMismatch" => mandate with { Currency = "USD" },
            "dateMismatch" => mandate with { CollectionDate = new DateOnly(2026, 11, 10) },
            "unknownType" => mandate with { Type = (DebitOrderMandateType)999 },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };

        var result = new DebitOrderEligibilityPolicy().Evaluate(
            transaction with { VerifiedMandate = mandate }, Context());

        Assert.Equal(DisputeEligibilityOutcome.RequiresManualReview, result.Outcome);
        Assert.Equal(DisputeReviewRoute.Investigation, result.ReviewRoute);
        Assert.Contains(reason, result.ReasonCodes);
        Assert.Null(result.OrdinaryReversalOutcome);
    }

    [Theory]
    [InlineData(12, "historical-demo", DisputeEligibilityOutcome.RequiresManualReview)]
    [InlineData(13, "proposed-demo", DisputeEligibilityOutcome.EligibleToSubmit)]
    [InlineData(14, "proposed-demo", DisputeEligibilityOutcome.EligibleToSubmit)]
    public void DebitOrder_EffectiveDatesAreInclusiveAndReturnSelectedVersion(
        int collectionDay, string version, DisputeEligibilityOutcome expected)
    {
        var collectionDate = new DateOnly(2026, 4, collectionDay);
        var settings = new DisputeEligibilityPolicySettings
        {
            DemoPoliciesEnabled = true,
            DebitOrder =
            [
                new()
                {
                    PolicyVersion = "proposed-demo", EffectiveFrom = new(2026, 4, 13),
                    WindowCalendarDays = 60, StandardReviewAmountLimit = 2000m
                },
                new()
                {
                    PolicyVersion = "historical-demo", EffectiveFrom = new(2026, 1, 1),
                    EffectiveTo = new(2026, 4, 12), WindowCalendarDays = 30, StandardReviewAmountLimit = 2000m
                }
            ]
        };
        var context = new DisputeEligibilityContext(
            new DateTimeOffset(collectionDate.AddDays(40).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), settings);

        var result = new DebitOrderEligibilityPolicy().Evaluate(
            Transaction(TransactionCategory.DebitOrder, collectionDate: collectionDate), context);

        Assert.Equal(expected, result.Outcome);
        Assert.Equal(version, result.PolicyVersion);
    }

    [Theory]
    [InlineData(1499.99, DisputeEligibilityOutcome.EligibleToSubmit)]
    [InlineData(1500, DisputeEligibilityOutcome.EligibleToSubmit)]
    [InlineData(1500.01, DisputeEligibilityOutcome.RequiresManualReview)]
    public void DebitOrder_ReviewThresholdIsConfigurableNotABankLimit(
        decimal amount, DisputeEligibilityOutcome expected)
    {
        var settings = new DisputeEligibilityPolicySettings
        {
            DemoPoliciesEnabled = true,
            DebitOrder = [new()
            {
                EffectiveFrom = new(2026, 4, 13), WindowCalendarDays = 60, StandardReviewAmountLimit = 1500m
            }]
        };

        var result = new DebitOrderEligibilityPolicy().Evaluate(
            Transaction(TransactionCategory.DebitOrder, amount: amount), Context(settings));

        Assert.Equal(expected, result.Outcome);
    }

    [Theory]
    [InlineData("2028-02-29", "2028-02-29T21:59:59Z", DisputeEligibilityOutcome.RequiresManualReview)]
    [InlineData("2028-02-29", "2028-02-29T22:00:00Z", DisputeEligibilityOutcome.EligibleToSubmit)]
    [InlineData("2028-02-29", "2028-04-29T21:59:59Z", DisputeEligibilityOutcome.EligibleToSubmit)]
    [InlineData("2028-02-29", "2028-04-29T22:00:00Z", DisputeEligibilityOutcome.RequiresManualReview)]
    public void DebitOrder_UsesSouthAfricanCalendarDatesAcrossLeapDay(
        string collectionDate, string instant, DisputeEligibilityOutcome expected)
    {
        var context = Context() with { EvaluatedAt = DateTimeOffset.Parse(instant) };

        var result = new DebitOrderEligibilityPolicy().Evaluate(
            Transaction(TransactionCategory.DebitOrder, collectionDate: DateOnly.Parse(collectionDate)), context);

        Assert.Equal(expected, result.Outcome);
    }

    [Fact]
    public void BankCharge_PostedWithInformation_IsEligible()
    {
        var result = new BankChargeEligibilityPolicy().Evaluate(
            Transaction(TransactionCategory.BankCharge), Context());

        Assert.Equal(DisputeEligibilityOutcome.EligibleToSubmit, result.Outcome);
        Assert.Empty(result.ReasonCodes);
    }

    [Fact]
    public void BankCharge_PendingCharge_IsIneligible()
    {
        var result = new BankChargeEligibilityPolicy().Evaluate(
            Transaction(TransactionCategory.BankCharge, status: TransactionStatus.PendingAuthorization),
            Context());

        Assert.Equal(DisputeEligibilityOutcome.Ineligible, result.Outcome);
        Assert.Contains("bankCharge.notPosted", result.ReasonCodes);
    }

    [Fact]
    public void BankCharge_UnsupportedCategory_IsIneligible()
    {
        var result = new BankChargeEligibilityPolicy().Evaluate(
            Transaction(TransactionCategory.CardPurchase), Context());

        Assert.Equal(DisputeEligibilityOutcome.Ineligible, result.Outcome);
        Assert.Contains("bankCharge.unsupportedType", result.ReasonCodes);
    }

    [Fact]
    public void BankCharge_AboveConfiguredReviewCondition_RequiresManualReview()
    {
        var result = new BankChargeEligibilityPolicy().Evaluate(
            Transaction(TransactionCategory.BankCharge, amount: 5000.01m), Context());

        Assert.Equal(DisputeEligibilityOutcome.RequiresManualReview, result.Outcome);
        Assert.Contains("bankCharge.configuredReviewCondition", result.ReasonCodes);
    }

    [Fact]
    public void BankCharge_MissingInformation_RequiresManualReview()
    {
        var result = new BankChargeEligibilityPolicy().Evaluate(
            Transaction(TransactionCategory.BankCharge, hasRequiredInformation: false), Context());

        Assert.Equal(DisputeEligibilityOutcome.RequiresManualReview, result.Outcome);
        Assert.Contains("transaction.informationMissing", result.ReasonCodes);
    }

    [Fact]
    public void BankCharge_WithoutConfiguredPolicy_RequiresManualReview()
    {
        var result = new BankChargeEligibilityPolicy().Evaluate(
            Transaction(TransactionCategory.BankCharge),
            new DisputeEligibilityContext(EvaluatedAt, new DisputeEligibilityPolicySettings()));

        Assert.Equal(DisputeEligibilityOutcome.RequiresManualReview, result.Outcome);
        Assert.Contains("policy.notConfigured", result.ReasonCodes);
    }

    private static DisputeTransaction Transaction(
        TransactionCategory category,
        decimal amount = 100m,
        TransactionStatus status = TransactionStatus.Posted,
        bool isDebit = true,
        bool hasRequiredInformation = true,
        DateTimeOffset? transactionDate = null,
        DateOnly? collectionDate = null,
        DebitOrderMandateType? mandateType = DebitOrderMandateType.EftDebitOrder,
        bool? mandateAuthorised = true,
        bool? mandateMatchesCollection = true) =>
        new(
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
            category,
            amount,
            "ZAR",
            transactionDate ?? EvaluatedAt.AddDays(-1),
            status,
            isDebit,
            hasRequiredInformation,
            collectionDate ?? DateOnly.FromDateTime(EvaluatedAt.AddDays(-1).UtcDateTime),
            mandateType,
            mandateAuthorised,
            mandateMatchesCollection,
            new DebitOrderMandate(mandateType, DebitOrderMandateStatus.Active, mandateAuthorised,
                mandateMatchesCollection == true ? amount : null, "ZAR",
                collectionDate ?? DateOnly.FromDateTime(EvaluatedAt.AddDays(-1).UtcDateTime)));

    private static DisputeEligibilityContext Context(
        DisputeEligibilityPolicySettings? settings = null,
        FrozenTimeProvider? clock = null) =>
        new(
            (clock ?? new FrozenTimeProvider(EvaluatedAt)).GetUtcNow(),
            settings ?? new DisputeEligibilityPolicySettings
            {
                PolicyVersion = "demo-v1",
                DemoPoliciesEnabled = true,
                Card = new CardEligibilitySettings
                {
                    DemoWindowEnabled = true,
                    DemoWindowCalendarDays = 120
                },
                DebitOrder =
                [
                    new DebitOrderEligibilitySettings
                    {
                        EffectiveFrom = new DateOnly(2026, 4, 13),
                        WindowCalendarDays = 60,
                        StandardReviewAmountLimit = 2000m
                    }
                ],
                BankCharge = new BankChargeEligibilitySettings { ManualReviewAboveAmount = 5000m }
            });

    private sealed class FrozenTimeProvider(DateTimeOffset instant) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => instant;
    }
}
