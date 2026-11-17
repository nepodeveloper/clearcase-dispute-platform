using ClearCase.Domain.Disputes;

namespace ClearCase.Api.IntegrationTests;

public sealed class DisputeLifecycleTests
{
    private static readonly DateTimeOffset OccurredAt = new(2026, 11, 17, 8, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(DisputeTransitionFamily.DebitOrder, "StartReview", "DebitOrderStartReview",
        "dispute.debit-order-transitioned.v1")]
    [InlineData(DisputeTransitionFamily.BankCharge, "Approve", "BankChargeApprove",
        "dispute.bank-charge-transitioned.v1")]
    [InlineData(DisputeTransitionFamily.Chargeback, "Initiate", "ChargebackInitiate",
        "dispute.chargeback-transitioned.v1")]
    public void AdvanceGeneratesCanonicalEventAndVersion(
        DisputeTransitionFamily family, string command, string eventType, string envelope)
    {
        var id = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var aggregate = new DisputeLifecycle(id, DisputeCaseStatus.Submitted,
            DisputeDecisionStatus.Pending, DisputeFinancialStatus.NotInitiated, 3, 3);
        var transitioned = aggregate.Advance(family, command,
            DisputeCaseStatus.UnderReview, DisputeDecisionStatus.Pending,
            DisputeFinancialStatus.NotInitiated, "Case moved to next review stage.",
            OccurredAt, eventId);

        Assert.Equal(4, transitioned.Next.Version);
        Assert.Equal(4, transitioned.Next.LastSequenceNumber);
        Assert.Equal(4, transitioned.Event.SequenceNumber);
        Assert.Equal(eventType, transitioned.Event.EventType);
        Assert.Equal(envelope, transitioned.EnvelopeEventType);
        Assert.Equal(eventId, transitioned.Event.Id);
        Assert.Equal(DisputeCaseStatus.Submitted, aggregate.CaseStatus);
        Assert.Equal(3, aggregate.Version);
    }

    [Theory]
    [InlineData(DisputeCaseStatus.Closed, DisputeDecisionStatus.Rejected,
        DisputeFinancialStatus.NotInitiated, DisputeCaseStatus.UnderReview,
        DisputeDecisionStatus.Pending, DisputeFinancialStatus.NotInitiated)]
    [InlineData(DisputeCaseStatus.Rejected, DisputeDecisionStatus.Rejected,
        DisputeFinancialStatus.NotInitiated, DisputeCaseStatus.UnderReview,
        DisputeDecisionStatus.Pending, DisputeFinancialStatus.NotInitiated)]
    [InlineData(DisputeCaseStatus.ReversalConfirmed, DisputeDecisionStatus.Approved,
        DisputeFinancialStatus.Confirmed, DisputeCaseStatus.UnderReview,
        DisputeDecisionStatus.Approved, DisputeFinancialStatus.Requested)]
    [InlineData(DisputeCaseStatus.UnderReview, DisputeDecisionStatus.Pending,
        DisputeFinancialStatus.NotInitiated, DisputeCaseStatus.UnderReview,
        DisputeDecisionStatus.Pending, DisputeFinancialStatus.Confirmed)]
    public void AdvanceRejectsForbiddenCrossCategoryTransitions(
        DisputeCaseStatus current, DisputeDecisionStatus decision, DisputeFinancialStatus financial,
        DisputeCaseStatus next, DisputeDecisionStatus nextDecision, DisputeFinancialStatus nextFinancial)
    {
        var aggregate = new DisputeLifecycle(Guid.NewGuid(), current, decision, financial, 2, 2);
        Assert.Throws<InvalidDisputeLifecycleTransitionException>(() =>
            aggregate.Advance(DisputeTransitionFamily.DebitOrder, "UnsafeCommand",
                next, nextDecision, nextFinancial, "This must not be committed.",
                OccurredAt, Guid.NewGuid()));
    }

    [Fact]
    public void SpecialistReferralAndResolutionUseCanonicalSafeEvents()
    {
        var original = new DisputeLifecycle(Guid.NewGuid(), DisputeCaseStatus.Submitted,
            DisputeDecisionStatus.Pending, DisputeFinancialStatus.NotInitiated, 1, 1);
        var referral = original.ReferForSpecialistReview(OccurredAt, Guid.NewGuid());
        Assert.Equal("SpecialistReview", referral.Event.EventType);
        Assert.Equal("dispute.specialist-review.v1", referral.EnvelopeEventType);
        Assert.Equal(DisputeCaseStatus.ManualReview, referral.Next.CaseStatus);
        var completed = referral.Next.ResumeAfterSpecialistReview(OccurredAt.AddMinutes(1), Guid.NewGuid());
        Assert.Equal("SpecialistReviewComplete", completed.Event.EventType);
        Assert.Equal("dispute.specialist-review-complete.v1", completed.EnvelopeEventType);
        Assert.Equal(DisputeCaseStatus.UnderReview, completed.Next.CaseStatus);
        Assert.Equal(3, completed.Next.Version);
        Assert.Equal(3, completed.Event.SequenceNumber);

        Assert.Throws<InvalidDisputeLifecycleTransitionException>(() =>
            original.ResumeAfterSpecialistReview(OccurredAt, Guid.NewGuid()));
    }

    [Fact]
    public void ClosingARejectedDisputePreservesDecisionAndFinancialTruth()
    {
        var aggregate = new DisputeLifecycle(Guid.NewGuid(), DisputeCaseStatus.Rejected,
            DisputeDecisionStatus.Rejected, DisputeFinancialStatus.NotInitiated, 7, 7);
        var transition = aggregate.Advance(DisputeTransitionFamily.BankCharge, "Close",
            DisputeCaseStatus.Closed, DisputeDecisionStatus.Rejected,
            DisputeFinancialStatus.NotInitiated, "Dispute closed.",
            OccurredAt, Guid.NewGuid());
        Assert.Equal(DisputeCaseStatus.Closed, transition.Next.CaseStatus);
        Assert.Equal(DisputeDecisionStatus.Rejected, transition.Next.DecisionStatus);
        Assert.Equal(DisputeFinancialStatus.NotInitiated, transition.Next.FinancialStatus);
    }
}
