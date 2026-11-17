import { AgentDisputeDetails } from './agent-disputes-api';

/**
 * Presentation hints, never an authorization or business-policy decision.
 * The API re-evaluates domain eligibility, bank-employee scope and expectedVersion.
 */
export interface StaffAction {
  command: string;
  label: string;
  description: string;
  confirmsMoney?: boolean;
  terminal?: boolean;
}

const action = (command: string, label: string, description: string,
  confirmsMoney = false, terminal = false): StaffAction =>
  ({ command, label, description, confirmsMoney, terminal });

export function suggestedStaffActions(item: AgentDisputeDetails): StaffAction[] {
  if (item.activeEscalation || item.caseStatus === 'Closed'
      || item.financialStatus === 'OutcomeUnknown' || item.financialStatus === 'Failed') return [];

  const status = item.caseStatus;
  const decision = item.decisionStatus;
  const money = item.financialStatus;

  switch (item.category) {
    case 'DebitOrder':
      if (status === 'Submitted' || status === 'ManualReview')
        return [action('StartReview', 'Start review', 'Begin investigating this debit order.')];
      if (status === 'UnderReview')
        return [
          action('VerifyMandate', 'Verify mandate', 'Check whether the mandate supports the transaction.'),
          action('CompleteInvestigation', 'Complete investigation', 'Finish policy and authorization checks.'),
          action('Reject', 'Reject dispute', 'Record a rejection after review.', false, true),
        ];
      if (status === 'MandateVerified')
        return [
          action('RequestReversal', 'Approve & request reversal', 'Approve the dispute and request a mock reversal.', true),
          action('Reject', 'Reject dispute', 'Record a rejection after review.', false, true),
        ];
      if (status === 'ReversalRequested' && money === 'Requested')
        return [action('ExecuteReversal', 'Process mock reversal', 'Send the existing reversal reference to the local mock gateway.', true)];
      if (status === 'Rejected' || status === 'ReversalConfirmed')
        return [action('Close', 'Close case', 'Close the completed investigation.', false, true)];
      return [];
    case 'BankCharge':
      if (status === 'Submitted' || status === 'ManualReview')
        return [action('StartReview', 'Start review', 'Begin the fee investigation.')];
      if (status === 'UnderReview' && decision === 'Pending')
        return [
          action('Approve', 'Approve dispute', 'Record the approval decision. No money moves.', false, true),
          action('Reject', 'Reject dispute', 'Record the rejection decision.', false, true),
        ];
      if (status === 'UnderReview' && decision === 'Approved' && money === 'NotInitiated')
        return [action('RequestAdjustment', 'Request fee adjustment', 'Request a mock fee refund.', true)];
      if (status === 'UnderReview' && decision === 'Approved' && money === 'Requested')
        return [action('ExecuteAdjustment', 'Process mock adjustment', 'Ask the mock gateway for the adjustment result.', true)];
      if (status === 'Rejected' || (status === 'UnderReview' && money === 'Confirmed'))
        return [action('Close', 'Close case', 'Close the completed investigation.', false, true)];
      return [];
    case 'CardPurchase': {
      const card = item.cardWorkflow?.status;
      if (!card || card === 'InitiationPending') {
        if (status === 'Submitted' || status === 'ManualReview')
          return [action('Initiate', 'Initiate chargeback', 'Create the mock card-network chargeback.', true)];
        return [];
      }
      if (card === 'Initiated')
        return [action('ForwardToAcquirer', 'Send to acquirer', 'Send the mock case for merchant review.', true)];
      if (card === 'MerchantReview')
        return [
          action('MerchantAccept', 'Merchant accepts', 'Record the simulated merchant acceptance.'),
          action('MerchantContest', 'Merchant contests', 'Record a simulated merchant challenge.'),
        ];
      if (card === 'MerchantContested')
        return [action('ReceiveRepresentment', 'Record evidence', 'Attach synthetic merchant evidence metadata.')];
      if (card === 'RepresentmentReceived')
        return [action('ReviewRepresentment', 'Review evidence', 'Mark the merchant evidence as reviewed.')];
      if (card === 'IssuerReview')
        return [
          action('Approve', 'Approve dispute', 'Record the issuer decision.', false, true),
          action('Reject', 'Reject dispute', 'Record the issuer decision.', false, true),
        ];
      if ((card === 'MerchantAccepted' || card === 'DecisionIssued')
          && decision === 'Approved' && money === 'NotInitiated')
        return [action('RequestSettlement', 'Request settlement', 'Request a simulated card settlement.', true)];
      if ((card === 'MerchantAccepted' || card === 'DecisionIssued')
          && decision === 'Approved' && money === 'Requested')
        return [action('ExecuteSettlement', 'Process mock settlement', 'Process through the mock card network.', true)];
      if ((card === 'MerchantAccepted' || card === 'DecisionIssued')
          && ((decision === 'Approved' && money === 'Confirmed')
            || (decision === 'Rejected' && money === 'NotInitiated')))
        return [action('Close', 'Close case', 'Close the recorded decision.', false, true)];
      return [];
    }
  }
}
