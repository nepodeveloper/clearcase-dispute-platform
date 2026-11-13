using ClearCase.Domain.Disputes;
using ClearCase.Domain.Transactions;

namespace ClearCase.Domain.Chargebacks;

public enum ChargebackStatus
{
    InitiationPending, Initiated, MerchantReview, MerchantAccepted, MerchantContested,
    RepresentmentReceived, IssuerReview, DecisionIssued, Closed
}

public enum ChargebackCommand
{
    Initiate, ForwardToAcquirer, MerchantAccept, MerchantContest, ReceiveRepresentment,
    ReviewRepresentment, Approve, Reject, RequestSettlement, ExecuteSettlement, Close
}

public enum CardNetworkOutcome { Succeeded, Failed, OutcomeUnknown }
public enum ChargebackEvidenceKind { Receipt, DeliveryConfirmation, AuthorizationRecord }
public sealed record RepresentmentEvidence(Guid Id, ChargebackEvidenceKind Kind, DateTimeOffset ReceivedAt);

public sealed class InvalidChargebackTransitionException()
    : Exception("This chargeback action is not allowed in the current state or requires further review.");

public sealed record Chargeback(
    Guid DisputeId, string Reference, string ReasonCode, ChargebackStatus Status,
    CardNetworkOutcome LastOperationOutcome, RepresentmentEvidence? Evidence)
{
    public static Chargeback Create(Guid disputeId, string reasonCode)
    {
        if (!DisputeReasonCatalog.IsValid(TransactionCategory.CardPurchase, reasonCode))
            throw new InvalidChargebackTransitionException();
        return new(disputeId, $"MOCK-CB-{disputeId:N}", reasonCode,
            ChargebackStatus.InitiationPending, CardNetworkOutcome.Succeeded, null);
    }

    public static bool UsesGateway(ChargebackCommand command) => command is
        ChargebackCommand.Initiate or ChargebackCommand.ForwardToAcquirer or ChargebackCommand.ExecuteSettlement;

    public string OperationReference(ChargebackCommand command) => $"MOCK-CARD-{DisputeId:N}-{command}";

    public void Validate(
        ChargebackCommand command, DisputeDecisionStatus decision, DisputeFinancialStatus financial,
        RepresentmentEvidence? evidence) => _ = NextStatus(command, decision, financial, evidence);

    private ChargebackStatus NextStatus(
        ChargebackCommand command, DisputeDecisionStatus decision, DisputeFinancialStatus financial,
        RepresentmentEvidence? evidence)
    {
        if (LastOperationOutcome != CardNetworkOutcome.Succeeded
            || (evidence is not null && command != ChargebackCommand.ReceiveRepresentment))
            throw new InvalidChargebackTransitionException();
        return command switch
        {
            ChargebackCommand.Initiate when Status == ChargebackStatus.InitiationPending => ChargebackStatus.Initiated,
            ChargebackCommand.ForwardToAcquirer when Status == ChargebackStatus.Initiated => ChargebackStatus.MerchantReview,
            ChargebackCommand.MerchantAccept when Status == ChargebackStatus.MerchantReview => ChargebackStatus.MerchantAccepted,
            ChargebackCommand.MerchantContest when Status == ChargebackStatus.MerchantReview => ChargebackStatus.MerchantContested,
            ChargebackCommand.ReceiveRepresentment when Status == ChargebackStatus.MerchantContested
                && evidence is not null && evidence.Id != Guid.Empty && Enum.IsDefined(evidence.Kind) =>
                ChargebackStatus.RepresentmentReceived,
            ChargebackCommand.ReviewRepresentment when Status == ChargebackStatus.RepresentmentReceived && Evidence is not null =>
                ChargebackStatus.IssuerReview,
            ChargebackCommand.Approve or ChargebackCommand.Reject when Status == ChargebackStatus.IssuerReview =>
                ChargebackStatus.DecisionIssued,
            ChargebackCommand.RequestSettlement when CanSettle(decision) && financial == DisputeFinancialStatus.NotInitiated => Status,
            ChargebackCommand.ExecuteSettlement when CanSettle(decision) && financial == DisputeFinancialStatus.Requested => Status,
            ChargebackCommand.Close when Status is ChargebackStatus.MerchantAccepted or ChargebackStatus.DecisionIssued
                && ((decision == DisputeDecisionStatus.Approved && financial == DisputeFinancialStatus.Confirmed)
                    || (decision == DisputeDecisionStatus.Rejected && financial == DisputeFinancialStatus.NotInitiated)) =>
                ChargebackStatus.Closed,
            _ => throw new InvalidChargebackTransitionException()
        };
    }

    public ChargebackTransition Apply(
        ChargebackCommand command, DisputeDecisionStatus decision, DisputeFinancialStatus financial,
        RepresentmentEvidence? evidence, CardNetworkOutcome outcome)
    {
        if (!Enum.IsDefined(outcome) || (outcome != CardNetworkOutcome.Succeeded && !UsesGateway(command)))
            throw new InvalidChargebackTransitionException();
        var nextStatus = NextStatus(command, decision, financial, evidence);
        var nextFinancial = command switch
        {
            ChargebackCommand.RequestSettlement => DisputeFinancialStatus.Requested,
            ChargebackCommand.ExecuteSettlement => outcome switch
            {
                CardNetworkOutcome.Succeeded => DisputeFinancialStatus.Confirmed,
                CardNetworkOutcome.Failed => DisputeFinancialStatus.Failed,
                _ => DisputeFinancialStatus.OutcomeUnknown
            },
            _ => financial
        };
        var nextDecision = command switch
        {
            ChargebackCommand.MerchantAccept or ChargebackCommand.Approve => DisputeDecisionStatus.Approved,
            ChargebackCommand.Reject => DisputeDecisionStatus.Rejected,
            _ => decision
        };
        var next = this with { Status = outcome == CardNetworkOutcome.Succeeded ? nextStatus : Status,
            LastOperationOutcome = outcome, Evidence = evidence ?? Evidence };
        var caseStatus = next.Status == ChargebackStatus.Closed ? DisputeCaseStatus.Closed
            : nextDecision == DisputeDecisionStatus.Rejected ? DisputeCaseStatus.Rejected : DisputeCaseStatus.UnderReview;
        var description = outcome switch
        {
            CardNetworkOutcome.Failed => "The mock card network failed the operation. No financial settlement is confirmed.",
            CardNetworkOutcome.OutcomeUnknown => "The mock card network outcome is unknown. Investigation is required; do not retry.",
            _ => command switch
            {
                ChargebackCommand.Initiate => "The issuer initiated a simulated chargeback. No funds have been credited.",
                ChargebackCommand.ForwardToAcquirer => "The mock network forwarded the chargeback to the acquirer for merchant review.",
                ChargebackCommand.MerchantAccept => "The merchant accepted the simulated chargeback. Settlement is not confirmed.",
                ChargebackCommand.MerchantContest => "The merchant contested the chargeback. The issuer has not made a final decision.",
                ChargebackCommand.ReceiveRepresentment => "Synthetic representment evidence metadata was received.",
                ChargebackCommand.ReviewRepresentment => "The issuer is reviewing the representment.",
                ChargebackCommand.Approve => "The issuer decided in the customer's favour. Settlement is not confirmed.",
                ChargebackCommand.Reject => "The issuer rejected the dispute after reviewing the representment.",
                ChargebackCommand.RequestSettlement => "Simulated settlement was requested. Funds are not confirmed.",
                ChargebackCommand.ExecuteSettlement => "The mock network explicitly confirmed settlement. No real money moved.",
                _ => "The chargeback dispute was closed."
            }
        };
        return new(next, caseStatus, nextDecision, nextFinancial, description);
    }

    private bool CanSettle(DisputeDecisionStatus decision) =>
        Status is ChargebackStatus.MerchantAccepted or ChargebackStatus.DecisionIssued
        && decision == DisputeDecisionStatus.Approved;
}

public sealed record ChargebackTransition(
    Chargeback Chargeback, DisputeCaseStatus CaseStatus, DisputeDecisionStatus DecisionStatus,
    DisputeFinancialStatus FinancialStatus, string Description);
