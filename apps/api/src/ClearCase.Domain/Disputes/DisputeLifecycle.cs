namespace ClearCase.Domain.Disputes;

/// <summary>
/// Shared lifecycle boundary for bank-charge, debit-order and chargeback transitions.
/// Category-specific state machines still decide which commands are legal; this
/// aggregate checks cross-category invariants and issues one immutable timeline event.
/// </summary>
public sealed record DisputeLifecycle(
    Guid DisputeId,
    DisputeCaseStatus CaseStatus,
    DisputeDecisionStatus DecisionStatus,
    DisputeFinancialStatus FinancialStatus,
    long Version,
    int LastSequenceNumber)
{
    public DisputeLifecycleTransition ReferForSpecialistReview(
        DateTimeOffset occurredAt, Guid eventId)
    {
        if (CaseStatus is DisputeCaseStatus.Closed or DisputeCaseStatus.Rejected
            or DisputeCaseStatus.ReversalConfirmed
            || DecisionStatus != DisputeDecisionStatus.Pending
            || FinancialStatus != DisputeFinancialStatus.NotInitiated)
            throw new InvalidDisputeLifecycleTransitionException();
        return SpecialistEvent("SpecialistReview",
            "Your case has been referred for additional review.",
            "dispute.specialist-review.v1", DisputeCaseStatus.ManualReview, occurredAt, eventId);
    }

    public DisputeLifecycleTransition ResumeAfterSpecialistReview(
        DateTimeOffset occurredAt, Guid eventId)
    {
        if (CaseStatus != DisputeCaseStatus.ManualReview
            || DecisionStatus != DisputeDecisionStatus.Pending
            || FinancialStatus != DisputeFinancialStatus.NotInitiated)
            throw new InvalidDisputeLifecycleTransitionException();
        return SpecialistEvent("SpecialistReviewComplete",
            "Additional review is complete and your case will continue to be investigated.",
            "dispute.specialist-review-complete.v1",
            DisputeCaseStatus.UnderReview, occurredAt, eventId);
    }

    private DisputeLifecycleTransition SpecialistEvent(
        string eventType, string description, string envelope,
        DisputeCaseStatus status, DateTimeOffset occurredAt, Guid eventId)
    {
        if (DisputeId == Guid.Empty || Version < 1 || LastSequenceNumber < 1 || eventId == Guid.Empty)
            throw new InvalidDisputeLifecycleTransitionException();
        var next = this with
        {
            CaseStatus = status,
            Version = checked(Version + 1),
            LastSequenceNumber = checked(LastSequenceNumber + 1)
        };
        return new DisputeLifecycleTransition(next,
            new DisputeEvent(eventId, next.LastSequenceNumber, eventType, status,
                description, occurredAt), envelope);
    }

    public DisputeLifecycleTransition Advance(
        DisputeTransitionFamily family, string command,
        DisputeCaseStatus newCaseStatus, DisputeDecisionStatus newDecisionStatus,
        DisputeFinancialStatus newFinancialStatus, string publicDescription,
        DateTimeOffset occurredAt, Guid eventId)
    {
        if (DisputeId == Guid.Empty || Version <= 0 || LastSequenceNumber < 1
            || eventId == Guid.Empty || string.IsNullOrWhiteSpace(command)
            || string.IsNullOrWhiteSpace(publicDescription))
            throw new InvalidDisputeLifecycleTransitionException();
        if (CaseStatus == DisputeCaseStatus.Closed && newCaseStatus != DisputeCaseStatus.Closed)
            throw new InvalidDisputeLifecycleTransitionException();
        if (CaseStatus == DisputeCaseStatus.Rejected &&
            newCaseStatus is not (DisputeCaseStatus.Rejected or DisputeCaseStatus.Closed))
            throw new InvalidDisputeLifecycleTransitionException();
        if (FinancialStatus == DisputeFinancialStatus.Confirmed
            && newFinancialStatus != DisputeFinancialStatus.Confirmed)
            throw new InvalidDisputeLifecycleTransitionException();
        if (newFinancialStatus == DisputeFinancialStatus.Confirmed
            && newDecisionStatus != DisputeDecisionStatus.Approved)
            throw new InvalidDisputeLifecycleTransitionException();
        var prefix = family switch
        {
            DisputeTransitionFamily.DebitOrder => "DebitOrder",
            DisputeTransitionFamily.BankCharge => "BankCharge",
            DisputeTransitionFamily.Chargeback => "Chargeback",
            DisputeTransitionFamily.CustomerWithdrawal => "Customer",
            _ => throw new InvalidDisputeLifecycleTransitionException()
        };
        var envelope = family switch
        {
            DisputeTransitionFamily.DebitOrder => "dispute.debit-order-transitioned.v1",
            DisputeTransitionFamily.BankCharge => "dispute.bank-charge-transitioned.v1",
            DisputeTransitionFamily.Chargeback => "dispute.chargeback-transitioned.v1",
            DisputeTransitionFamily.CustomerWithdrawal => "dispute.withdrawn.v1",
            _ => throw new InvalidDisputeLifecycleTransitionException()
        };
        return new DisputeLifecycleTransition(
            this with
            {
                CaseStatus = newCaseStatus,
                DecisionStatus = newDecisionStatus,
                FinancialStatus = newFinancialStatus,
                Version = checked(Version + 1),
                LastSequenceNumber = checked(LastSequenceNumber + 1)
            },
            new DisputeEvent(eventId, checked(LastSequenceNumber + 1), prefix + command,
                newCaseStatus, publicDescription, occurredAt),
            envelope);
    }
}

public enum DisputeTransitionFamily { DebitOrder, BankCharge, Chargeback, CustomerWithdrawal }

public sealed class InvalidDisputeLifecycleTransitionException()
    : Exception("This dispute lifecycle transition violates cross-category case invariants.");

public sealed record DisputeLifecycleTransition(
    DisputeLifecycle Next, DisputeEvent Event, string EnvelopeEventType);
