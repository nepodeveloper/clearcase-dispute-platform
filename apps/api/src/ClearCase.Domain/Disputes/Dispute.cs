using ClearCase.Domain.DisputeEligibility;
using ClearCase.Domain.Transactions;

namespace ClearCase.Domain.Disputes;

/// <summary>Customer-facing case lifecycle; independent of the investigation decision and money movement.</summary>
public enum DisputeCaseStatus
{
    Submitted,
    ManualReview,
    Closed,
    UnderReview,
    MandateVerified,
    ReversalRequested,
    ReversalConfirmed,
    Rejected
}

/// <summary>Investigation decision, independent of financial confirmation.</summary>
public enum DisputeDecisionStatus
{
    Pending,
    Approved,
    Rejected
}

/// <summary>Money-movement status; submission never initiates a financial operation.</summary>
public enum DisputeFinancialStatus
{
    NotInitiated,
    Requested,
    Confirmed,
    Failed,
    OutcomeUnknown
}

public sealed record DisputeEvent(
    Guid Id,
    int SequenceNumber,
    string EventType,
    DisputeCaseStatus CaseStatus,
    string PublicDescription,
    DateTimeOffset OccurredAt);

public sealed class Dispute
{
    public const int DescriptionMinLength = 15;
    public const int DescriptionMaxLength = 2000;
    public const string SubmittedEventType = "DisputeSubmitted";

    private Dispute(
        Guid id, string reference, Guid customerId, Guid transactionId, TransactionCategory category,
        string reasonCode, string description, DisputeCaseStatus caseStatus,
        DisputeEligibilityResult eligibility, DisputeEvent initialEvent)
    {
        Id = id;
        Reference = reference;
        CustomerId = customerId;
        TransactionId = transactionId;
        Category = category;
        ReasonCode = reasonCode;
        Description = description;
        CaseStatus = caseStatus;
        Eligibility = eligibility;
        CreatedAt = eligibility.EvaluatedAt;
        Timeline = [initialEvent];
    }

    public Guid Id { get; }
    public string Reference { get; }
    public Guid CustomerId { get; }
    public Guid TransactionId { get; }
    public TransactionCategory Category { get; }
    public string ReasonCode { get; }
    public string Description { get; }
    public DisputeCaseStatus CaseStatus { get; }
    public DisputeDecisionStatus DecisionStatus => DisputeDecisionStatus.Pending;
    public DisputeFinancialStatus FinancialStatus => DisputeFinancialStatus.NotInitiated;
    public DisputeEligibilityResult Eligibility { get; }
    public DateTimeOffset CreatedAt { get; }
    public IReadOnlyList<DisputeEvent> Timeline { get; }

    /// <summary>
    /// Opens a case for an owned transaction. An ineligible decision, unknown reason or invalid
    /// description is rejected here so no code path can persist one.
    /// </summary>
    public static Dispute Submit(
        Guid id, string reference, Guid customerId, DisputeTransaction transaction,
        string reasonCode, string description, DisputeEligibilityResult eligibility, Guid initialEventId)
    {
        if (eligibility.Outcome == DisputeEligibilityOutcome.Ineligible)
            throw new InvalidOperationException("An ineligible transaction cannot be disputed.");
        if (!DisputeReasonCatalog.IsValid(transaction.Category, reasonCode))
            throw new ArgumentException("The reason is not valid for the transaction category.", nameof(reasonCode));
        if (description.Length is < DescriptionMinLength or > DescriptionMaxLength)
            throw new ArgumentException("The description length is invalid.", nameof(description));

        var manualReview = eligibility.Outcome == DisputeEligibilityOutcome.RequiresManualReview;
        var caseStatus = manualReview ? DisputeCaseStatus.ManualReview : DisputeCaseStatus.Submitted;
        var initialEvent = new DisputeEvent(
            initialEventId, 1, SubmittedEventType, caseStatus,
            manualReview
                ? "Your dispute was received and will be reviewed by a specialist."
                : "Your dispute was received and is awaiting review.",
            eligibility.EvaluatedAt);
        return new Dispute(id, reference, customerId, transaction.Id, transaction.Category, reasonCode,
            description, caseStatus, eligibility, initialEvent);
    }
}
