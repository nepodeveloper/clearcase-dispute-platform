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
            _ => throw new InvalidDisputeLifecycleTransitionException()
        };
        var envelope = family switch
        {
            DisputeTransitionFamily.DebitOrder => "dispute.debit-order-transitioned.v1",
            DisputeTransitionFamily.BankCharge => "dispute.bank-charge-transitioned.v1",
            DisputeTransitionFamily.Chargeback => "dispute.chargeback-transitioned.v1",
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

public enum DisputeTransitionFamily { DebitOrder, BankCharge, Chargeback }

public sealed class InvalidDisputeLifecycleTransitionException()
    : Exception("This dispute lifecycle transition violates cross-category case invariants.");

public sealed record DisputeLifecycleTransition(
    DisputeLifecycle Next, DisputeEvent Event, string EnvelopeEventType);
