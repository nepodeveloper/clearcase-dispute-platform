namespace ClearCase.Domain.Disputes;

public enum DisputeEscalationStatus
{
    Open,
    Acknowledged,
    Resolved
}

public enum DisputeEscalationAction
{
    Created,
    Acknowledged,
    Resolved
}

public static class DisputeEscalationRules
{
    public static readonly IReadOnlyList<string> Reasons =
    [
        "Additional investigation required",
        "Customer authorization dispute",
        "Mandate mismatch",
        "Policy exception review"
    ];

    public static bool IsValidReason(string value) => Reasons.Contains(value, StringComparer.Ordinal);

    public static bool CanCreate(
        DisputeCaseStatus caseStatus,
        DisputeDecisionStatus decisionStatus,
        DisputeFinancialStatus financialStatus) =>
        caseStatus is not (DisputeCaseStatus.Closed or DisputeCaseStatus.Rejected
            or DisputeCaseStatus.ReversalConfirmed)
        && decisionStatus == DisputeDecisionStatus.Pending
        && financialStatus == DisputeFinancialStatus.NotInitiated;

    public static bool CanAcknowledge(
        DisputeEscalationStatus status, Guid creatorId, Guid supervisorId) =>
        status == DisputeEscalationStatus.Open && creatorId != supervisorId;

    public static bool CanResolve(
        DisputeEscalationStatus status, Guid creatorId, Guid supervisorId,
        DisputeCaseStatus caseStatus, DisputeDecisionStatus decisionStatus,
        DisputeFinancialStatus financialStatus) =>
        status == DisputeEscalationStatus.Acknowledged
        && creatorId != supervisorId
        // Only an active specialist review with an undecided, financially untouched
        // dispute can be returned to UnderReview.
        && caseStatus == DisputeCaseStatus.ManualReview
        && decisionStatus == DisputeDecisionStatus.Pending
        && financialStatus == DisputeFinancialStatus.NotInitiated;
}
