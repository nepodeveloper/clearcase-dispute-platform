using ClearCase.Domain.Transactions;

namespace ClearCase.Domain.DisputeEligibility;

public enum DisputeEligibilityOutcome
{
    EligibleToSubmit,
    RequiresManualReview,
    Ineligible
}

public enum DisputeReviewRoute
{
    Investigation
}

public sealed record DisputeEligibilityResult(
    DisputeEligibilityOutcome Outcome,
    IReadOnlyList<string> ReasonCodes,
    string Explanation,
    string PolicyVersion,
    DateTimeOffset EvaluatedAt)
{
    public DisputeEligibilityOutcome? OrdinaryReversalOutcome { get; init; }

    public DisputeReviewRoute? ReviewRoute { get; init; }
}

public sealed record DisputeEligibilityContext(
    DateTimeOffset EvaluatedAt,
    DisputeEligibilityPolicySettings Settings,
    string? PolicyVersion = null);

public interface IDisputeEligibilityPolicy
{
    TransactionCategory Category { get; }

    DisputeEligibilityResult Evaluate(
        DisputeTransaction transaction,
        DisputeEligibilityContext context);
}

public sealed class DisputeEligibilityPolicySettings
{
    public string PolicyVersion { get; init; } = "unconfigured-v1";

    public bool DemoPoliciesEnabled { get; init; }

    public CardEligibilitySettings Card { get; init; } = new();

    public List<DebitOrderEligibilitySettings> DebitOrder { get; init; } = [];

    public BankChargeEligibilitySettings BankCharge { get; init; } = new();
}

public sealed class CardEligibilitySettings
{
    public bool DemoWindowEnabled { get; init; }

    public int DemoWindowCalendarDays { get; init; }
}

public sealed class DebitOrderEligibilitySettings
{
    public string? PolicyVersion { get; init; }

    public DateOnly EffectiveFrom { get; init; }

    public DateOnly? EffectiveTo { get; init; }

    public int WindowCalendarDays { get; init; }

    public decimal StandardReviewAmountLimit { get; init; }
}

public sealed class BankChargeEligibilitySettings
{
    public decimal? ManualReviewAboveAmount { get; init; }
}

internal static class EligibilityResult
{
    public static DisputeEligibilityResult Create(
        DisputeEligibilityOutcome outcome,
        DisputeEligibilityContext context,
        string explanation,
        params string[] reasonCodes) =>
        new(outcome, reasonCodes, explanation,
            context.PolicyVersion ?? context.Settings.PolicyVersion, context.EvaluatedAt);

    public static DisputeEligibilityResult Eligible(DisputeEligibilityContext context) =>
        Create(DisputeEligibilityOutcome.EligibleToSubmit, context,
            "This transaction can be submitted for review.");

    public static DisputeEligibilityResult ManualReview(
        DisputeEligibilityContext context,
        string reasonCode,
        string explanation) =>
        Create(DisputeEligibilityOutcome.RequiresManualReview, context, explanation, reasonCode);

    public static DisputeEligibilityResult Ineligible(
        DisputeEligibilityContext context,
        string reasonCode,
        string explanation) =>
        Create(DisputeEligibilityOutcome.Ineligible, context, explanation, reasonCode);
}
