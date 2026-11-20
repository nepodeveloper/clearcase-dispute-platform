namespace ClearCase.Domain.Disputes;

public static class DisputeWithdrawalRules
{
    public const int ReasonMaxLength = 500;
    public const string EventType = "CustomerWithdrawn";
    public const string PublicDescription = "You withdrew this dispute. No payment adjustment was made.";

    /// <summary>
    /// A customer may only walk away from an undecided case that no money has been requested for,
    /// that supervisors are not reviewing, and (card purchases) that has not been charged back.
    /// </summary>
    public static bool CanWithdraw(
        DisputeCaseStatus caseStatus, DisputeDecisionStatus decisionStatus,
        DisputeFinancialStatus financialStatus, bool hasActiveEscalation, bool hasChargebackRecord) =>
        caseStatus is not (DisputeCaseStatus.Closed or DisputeCaseStatus.Rejected)
        && decisionStatus == DisputeDecisionStatus.Pending
        && financialStatus == DisputeFinancialStatus.NotInitiated
        && !hasActiveEscalation
        && !hasChargebackRecord;
}

public sealed class DisputeWithdrawalNotAllowedException()
    : Exception("The dispute can no longer be withdrawn.");
