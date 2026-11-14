namespace ClearCase.Domain.Disputes;

public enum DebitOrderCommand
{
    StartReview, VerifyMandate, CompleteInvestigation, RequestReversal, ExecuteReversal, ReconcileReversal, Reject, Close
}

public enum DebitOrderGatewayOutcome { Confirmed, Failed, Unknown }

public sealed class InvalidDebitOrderTransitionException()
    : Exception("This action is not allowed in the current state or requires further mandate and policy investigation.");

public sealed record DebitOrderState(
    DisputeCaseStatus Case, DisputeDecisionStatus Decision, DisputeFinancialStatus Financial)
{
    public DebitOrderTransition Apply(DebitOrderCommand command, bool eligible, bool authorizationDisputed)
    {
        var next = command switch
        {
            DebitOrderCommand.StartReview when Case is DisputeCaseStatus.Submitted or DisputeCaseStatus.ManualReview =>
                new DebitOrderTransition(this with { Case = DisputeCaseStatus.UnderReview }, "The debit order is under review."),
            DebitOrderCommand.VerifyMandate when Case == DisputeCaseStatus.UnderReview =>
                eligible && !authorizationDisputed
                    ? Verified()
                    : new(this, "Manual investigation is required before any simulated reversal."),
            DebitOrderCommand.CompleteInvestigation when Case == DisputeCaseStatus.UnderReview && eligible =>
                Verified(),
            DebitOrderCommand.RequestReversal when Case == DisputeCaseStatus.MandateVerified && eligible =>
                new(this with { Case = DisputeCaseStatus.ReversalRequested,
                    Decision = DisputeDecisionStatus.Approved, Financial = DisputeFinancialStatus.Requested },
                    "The case is approved and a simulated reversal was requested. Funds are not confirmed."),
            DebitOrderCommand.ExecuteReversal when Case == DisputeCaseStatus.ReversalRequested
                && Financial == DisputeFinancialStatus.Requested && eligible =>
                new(this, "The simulated reversal is being processed."),
            DebitOrderCommand.Reject when Case is DisputeCaseStatus.UnderReview or DisputeCaseStatus.MandateVerified
                || (Case == DisputeCaseStatus.ReversalRequested && Financial == DisputeFinancialStatus.Failed) =>
                new(this with { Case = DisputeCaseStatus.Rejected, Decision = DisputeDecisionStatus.Rejected },
                    "The dispute was rejected after simulated review."),
            DebitOrderCommand.Close when Case is DisputeCaseStatus.Rejected or DisputeCaseStatus.ReversalConfirmed =>
                new(this with { Case = DisputeCaseStatus.Closed }, "The dispute was closed."),
            _ => throw new InvalidDebitOrderTransitionException()
        };
        return next;
    }

    public DebitOrderTransition CompleteReversal(DebitOrderGatewayOutcome outcome)
    {
        if (Case != DisputeCaseStatus.ReversalRequested || Financial != DisputeFinancialStatus.Requested)
            throw new InvalidDebitOrderTransitionException();
        return outcome switch
        {
            DebitOrderGatewayOutcome.Confirmed => new(this with {
                Case = DisputeCaseStatus.ReversalConfirmed, Financial = DisputeFinancialStatus.Confirmed },
                "The mock gateway confirmed the simulated reversal. No real money moved."),
            DebitOrderGatewayOutcome.Failed => new(this with { Financial = DisputeFinancialStatus.Failed },
                "The mock gateway declined the reversal. No reversal is confirmed."),
            DebitOrderGatewayOutcome.Unknown => new(this with { Financial = DisputeFinancialStatus.OutcomeUnknown },
                "The simulated reversal outcome is unknown. Investigation is required; do not retry."),
            _ => throw new ArgumentOutOfRangeException(nameof(outcome))
        };
    }

    public DebitOrderTransition ReconcileReversal(DebitOrderGatewayOutcome outcome)
    {
        if (Case != DisputeCaseStatus.ReversalRequested || Financial != DisputeFinancialStatus.OutcomeUnknown)
            throw new InvalidDebitOrderTransitionException();
        return outcome switch
        {
            DebitOrderGatewayOutcome.Confirmed => new(this with {
                Case = DisputeCaseStatus.ReversalConfirmed, Financial = DisputeFinancialStatus.Confirmed },
                "The original reversal reference was authoritatively confirmed. No new reversal was submitted."),
            DebitOrderGatewayOutcome.Failed => new(this with { Financial = DisputeFinancialStatus.Failed },
                "The original reversal reference was authoritatively declined. No new reversal was submitted."),
            DebitOrderGatewayOutcome.Unknown => new(this,
                "The original reversal outcome remains unknown. No new reversal was submitted."),
            _ => throw new ArgumentOutOfRangeException(nameof(outcome))
        };
    }

    private DebitOrderTransition Verified() => new(this with { Case = DisputeCaseStatus.MandateVerified },
        "Mandate and policy checks passed after simulated review. No reversal has been requested.");
}

public sealed record DebitOrderTransition(DebitOrderState State, string Description);
