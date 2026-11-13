namespace ClearCase.Domain.Disputes;

public enum BankChargeCommand { StartReview, Approve, Reject, RequestAdjustment, ExecuteAdjustment, Close }
public enum BankChargeAdjustmentOutcome { Confirmed, Failed, Unknown }
public sealed class InvalidBankChargeTransitionException()
    : Exception("The bank-charge action is not allowed or requires further investigation.");

public sealed record BankChargeState(
    DisputeCaseStatus Case, DisputeDecisionStatus Decision, DisputeFinancialStatus Financial)
{
    public BankChargeTransition Apply(BankChargeCommand command, bool ready) => command switch
    {
        BankChargeCommand.StartReview when Case is DisputeCaseStatus.Submitted or DisputeCaseStatus.ManualReview =>
            new(this with { Case = DisputeCaseStatus.UnderReview }, "The bank fee is being investigated internally."),
        BankChargeCommand.Approve when Case == DisputeCaseStatus.UnderReview && Decision == DisputeDecisionStatus.Pending && ready =>
            new(this with { Decision = DisputeDecisionStatus.Approved }, "The fee dispute was approved. No adjustment has been requested."),
        BankChargeCommand.Reject when Case == DisputeCaseStatus.UnderReview && Decision == DisputeDecisionStatus.Pending =>
            new(this with { Case = DisputeCaseStatus.Rejected, Decision = DisputeDecisionStatus.Rejected },
                "The fee dispute was rejected after internal review."),
        BankChargeCommand.RequestAdjustment when CanAdjust && Financial == DisputeFinancialStatus.NotInitiated && ready =>
            new(this with { Financial = DisputeFinancialStatus.Requested }, "A simulated fee adjustment was requested. Funds are not confirmed."),
        BankChargeCommand.ExecuteAdjustment when CanAdjust && Financial == DisputeFinancialStatus.Requested && ready =>
            new(this, "The simulated fee adjustment is being processed."),
        BankChargeCommand.Close when (Case == DisputeCaseStatus.Rejected && Financial == DisputeFinancialStatus.NotInitiated)
            || (CanAdjust && Financial == DisputeFinancialStatus.Confirmed) =>
            new(this with { Case = DisputeCaseStatus.Closed }, "The bank-fee dispute was closed."),
        _ => throw new InvalidBankChargeTransitionException()
    };

    public BankChargeTransition CompleteAdjustment(BankChargeAdjustmentOutcome outcome)
    {
        if (!CanAdjust || Financial != DisputeFinancialStatus.Requested)
            throw new InvalidBankChargeTransitionException();
        return outcome switch
        {
            BankChargeAdjustmentOutcome.Confirmed => new(this with { Financial = DisputeFinancialStatus.Confirmed },
                "The mock bank confirmed the fee adjustment. No real money moved."),
            BankChargeAdjustmentOutcome.Failed => new(this with { Financial = DisputeFinancialStatus.Failed },
                "The mock fee adjustment failed. No financial adjustment is confirmed."),
            BankChargeAdjustmentOutcome.Unknown => new(this with { Financial = DisputeFinancialStatus.OutcomeUnknown },
                "The mock fee adjustment outcome is unknown. Investigation is required; do not retry."),
            _ => throw new ArgumentOutOfRangeException(nameof(outcome))
        };
    }

    private bool CanAdjust => Case == DisputeCaseStatus.UnderReview && Decision == DisputeDecisionStatus.Approved;
}

public sealed record BankChargeTransition(BankChargeState State, string Description);
