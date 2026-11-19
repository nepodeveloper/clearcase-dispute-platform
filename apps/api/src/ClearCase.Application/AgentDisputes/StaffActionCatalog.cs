using ClearCase.Domain.Chargebacks;
using ClearCase.Domain.Disputes;
using ClearCase.Domain.Transactions;

namespace ClearCase.Application.AgentDisputes;

/// <summary>A staff command the case can accept now. The command handler still re-validates every request.</summary>
public sealed record AvailableStaffAction(
    string Command, string Label, string Description, bool MovesMoney = false, bool Terminal = false);

/// <summary>
/// Single server-side source for which staff commands are offered for a case, so clients render
/// the list instead of mirroring the category workflows.
/// </summary>
public static class StaffActionCatalog
{
    private static readonly HashSet<string> ExternalUpdateCommands =
    [
        nameof(ChargebackCommand.MerchantAccept),
        nameof(ChargebackCommand.MerchantContest),
        nameof(ChargebackCommand.ReceiveRepresentment)
    ];

    public static IReadOnlyList<AvailableStaffAction> For(
        AgentDisputeDetails item, bool simulatedExternalUpdatesAllowed)
    {
        if (item.ActiveEscalation is not null || item.CaseStatus == DisputeCaseStatus.Closed)
            return [];

        var actions = item.Category switch
        {
            TransactionCategory.DebitOrder => DebitOrder(item),
            TransactionCategory.BankCharge => BankCharge(item),
            TransactionCategory.CardPurchase => CardPurchase(item),
            _ => []
        };
        return simulatedExternalUpdatesAllowed
            ? actions
            : actions.Where(action => !ExternalUpdateCommands.Contains(action.Command)).ToArray();
    }

    private static AvailableStaffAction[] DebitOrder(AgentDisputeDetails item)
    {
        var status = item.CaseStatus;
        var money = item.FinancialStatus;
        if (money == DisputeFinancialStatus.OutcomeUnknown)
            return status == DisputeCaseStatus.ReversalRequested && item.ReversalOperationReference is not null
                ? [new("ReconcileReversal", "Reconcile reversal",
                    "Ask the gateway for the status of the original reversal reference. No new reversal is sent.",
                    MovesMoney: true)]
                : [];
        if (money == DisputeFinancialStatus.Failed)
            return status == DisputeCaseStatus.ReversalRequested ? [Reject("Record a rejection after the failed reversal.")] : [];

        return status switch
        {
            DisputeCaseStatus.Submitted or DisputeCaseStatus.ManualReview =>
                [new("StartReview", "Start review", "Begin investigating this debit order.")],
            DisputeCaseStatus.UnderReview =>
            [
                new("VerifyMandate", "Verify mandate", "Check whether the mandate supports the transaction."),
                new("CompleteInvestigation", "Complete investigation", "Finish policy and authorization checks."),
                Reject("Record a rejection after review.")
            ],
            DisputeCaseStatus.MandateVerified =>
            [
                new("RequestReversal", "Approve & request reversal",
                    "Approve the dispute and request a mock reversal.", MovesMoney: true),
                Reject("Record a rejection after review.")
            ],
            DisputeCaseStatus.ReversalRequested when money == DisputeFinancialStatus.Requested =>
                [new("ExecuteReversal", "Process mock reversal",
                    "Send the existing reversal reference to the local mock gateway.", MovesMoney: true)],
            DisputeCaseStatus.Rejected or DisputeCaseStatus.ReversalConfirmed =>
                [Close("Close the completed investigation.")],
            _ => []
        };
    }

    private static AvailableStaffAction[] BankCharge(AgentDisputeDetails item)
    {
        var status = item.CaseStatus;
        var decision = item.DecisionStatus;
        var money = item.FinancialStatus;
        if (money is DisputeFinancialStatus.OutcomeUnknown or DisputeFinancialStatus.Failed)
            return [];

        if (status is DisputeCaseStatus.Submitted or DisputeCaseStatus.ManualReview)
            return [new("StartReview", "Start review", "Begin the fee investigation.")];
        if (status == DisputeCaseStatus.UnderReview && decision == DisputeDecisionStatus.Pending)
            return
            [
                new("Approve", "Approve dispute", "Record the approval decision. No money moves.", Terminal: true),
                new("Reject", "Reject dispute", "Record the rejection decision.", Terminal: true)
            ];
        if (status == DisputeCaseStatus.UnderReview && decision == DisputeDecisionStatus.Approved
            && money == DisputeFinancialStatus.NotInitiated)
            return [new("RequestAdjustment", "Request fee adjustment", "Request a mock fee refund.", MovesMoney: true)];
        if (status == DisputeCaseStatus.UnderReview && decision == DisputeDecisionStatus.Approved
            && money == DisputeFinancialStatus.Requested)
            return [new("ExecuteAdjustment", "Process mock adjustment",
                "Ask the mock gateway for the adjustment result.", MovesMoney: true)];
        if (status == DisputeCaseStatus.Rejected
            || (status == DisputeCaseStatus.UnderReview && money == DisputeFinancialStatus.Confirmed))
            return [Close("Close the completed investigation.")];
        return [];
    }

    private static AvailableStaffAction[] CardPurchase(AgentDisputeDetails item)
    {
        var status = item.CaseStatus;
        var decision = item.DecisionStatus;
        var money = item.FinancialStatus;
        if (money is DisputeFinancialStatus.OutcomeUnknown or DisputeFinancialStatus.Failed)
            return [];

        var card = item.CardWorkflow?.Status;
        if (card is null or nameof(ChargebackStatus.InitiationPending))
            return status is DisputeCaseStatus.Submitted or DisputeCaseStatus.ManualReview
                ? [new("Initiate", "Initiate chargeback", "Create the mock card-network chargeback.", MovesMoney: true)]
                : [];

        var settled = card is nameof(ChargebackStatus.MerchantAccepted)
            or nameof(ChargebackStatus.DecisionIssued);
        return card switch
        {
            nameof(ChargebackStatus.Initiated) =>
                [new("ForwardToAcquirer", "Send to acquirer",
                    "Send the mock case for merchant review.", MovesMoney: true)],
            nameof(ChargebackStatus.MerchantReview) =>
            [
                new("MerchantAccept", "Merchant accepts", "Record the simulated merchant acceptance."),
                new("MerchantContest", "Merchant contests", "Record a simulated merchant challenge.")
            ],
            nameof(ChargebackStatus.MerchantContested) =>
                [new("ReceiveRepresentment", "Record evidence", "Attach synthetic merchant evidence metadata.")],
            nameof(ChargebackStatus.RepresentmentReceived) =>
                [new("ReviewRepresentment", "Review evidence", "Mark the merchant evidence as reviewed.")],
            nameof(ChargebackStatus.IssuerReview) =>
            [
                new("Approve", "Approve dispute", "Record the issuer decision.", Terminal: true),
                new("Reject", "Reject dispute", "Record the issuer decision.", Terminal: true)
            ],
            _ when settled && decision == DisputeDecisionStatus.Approved && money == DisputeFinancialStatus.NotInitiated =>
                [new("RequestSettlement", "Request settlement", "Request a simulated card settlement.", MovesMoney: true)],
            _ when settled && decision == DisputeDecisionStatus.Approved && money == DisputeFinancialStatus.Requested =>
                [new("ExecuteSettlement", "Process mock settlement",
                    "Process through the mock card network.", MovesMoney: true)],
            _ when settled && ((decision == DisputeDecisionStatus.Approved && money == DisputeFinancialStatus.Confirmed)
                || (decision == DisputeDecisionStatus.Rejected && money == DisputeFinancialStatus.NotInitiated)) =>
                [Close("Close the recorded decision.")],
            _ => []
        };
    }

    private static AvailableStaffAction Reject(string description) =>
        new("Reject", "Reject dispute", description, Terminal: true);

    private static AvailableStaffAction Close(string description) =>
        new("Close", "Close case", description, Terminal: true);
}
