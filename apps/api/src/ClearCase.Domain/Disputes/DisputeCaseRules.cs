namespace ClearCase.Domain.Disputes;

/// <summary>Canonical case completion semantics: a decision is not the same as closure.</summary>
public static class DisputeCaseRules
{
    public static bool IsOpen(DisputeCaseStatus status) => status != DisputeCaseStatus.Closed;
    public static bool IsClosed(DisputeCaseStatus status) => status == DisputeCaseStatus.Closed;
    public static bool HasFinalDecision(DisputeDecisionStatus status) =>
        status is DisputeDecisionStatus.Approved or DisputeDecisionStatus.Rejected;
}
