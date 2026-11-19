using ClearCase.Domain.Disputes;

namespace ClearCase.Api.IntegrationTests;

public sealed class DisputeCaseRulesTests
{
    [Theory]
    [InlineData(DisputeCaseStatus.Submitted)]
    [InlineData(DisputeCaseStatus.ManualReview)]
    [InlineData(DisputeCaseStatus.UnderReview)]
    [InlineData(DisputeCaseStatus.MandateVerified)]
    [InlineData(DisputeCaseStatus.ReversalRequested)]
    [InlineData(DisputeCaseStatus.ReversalConfirmed)]
    [InlineData(DisputeCaseStatus.Rejected)]
    public void OnlyClosedIsFinalCaseStatus(DisputeCaseStatus status)
    {
        Assert.True(DisputeCaseRules.IsOpen(status));
        Assert.False(DisputeCaseRules.IsClosed(status));
    }

    [Fact]
    public void ApprovalOrRejectionIsNotClosure()
    {
        Assert.True(DisputeCaseRules.IsClosed(DisputeCaseStatus.Closed));
        Assert.False(DisputeCaseRules.IsOpen(DisputeCaseStatus.Closed));
        Assert.True(DisputeCaseRules.HasFinalDecision(DisputeDecisionStatus.Approved));
        Assert.True(DisputeCaseRules.HasFinalDecision(DisputeDecisionStatus.Rejected));
        Assert.False(DisputeCaseRules.HasFinalDecision(DisputeDecisionStatus.Pending));
    }
}
