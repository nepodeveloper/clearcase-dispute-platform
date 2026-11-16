import { DatePipe } from '@angular/common';
import { Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { DecimalMoney } from '../transactions/decimal-money';
import { transactionTypeLabel } from '../transactions/transaction-type-label';
import { Timeline, TimelineEvent } from '../ui/ui-components';
import { ChargebackStatus, Dispute, DisputeCaseStatus } from './dispute-api';

export function disputeStatusLabel(status: DisputeCaseStatus): string {
  const labels: Record<DisputeCaseStatus, string> = {
    Submitted: 'Submitted - awaiting review',
    ManualReview: 'Referred for specialist review',
    Closed: 'Closed',
    UnderReview: 'Under review',
    MandateVerified: 'Payment authorization checked',
    ReversalRequested: 'Refund requested',
    ReversalConfirmed: 'Demo refund confirmed',
    Rejected: 'Review complete - not approved',
  };
  return labels[status];
}

export function disputeDecisionLabel(status: Dispute['decisionStatus']): string {
  return { Pending: 'Awaiting a decision', Approved: 'Approved after review', Rejected: 'Not approved after review' }[status];
}

export function disputeFinancialLabel(status: Dispute['financialStatus']): string {
  switch (status) {
    case 'NotInitiated': return 'Not started';
    case 'Requested': return 'Requested - not confirmed';
    case 'OutcomeUnknown': return 'Outcome unknown - not confirmed';
    case 'Failed': return 'Not completed - no refund confirmed';
    case 'Confirmed': return 'Demo refund confirmed - no real money moved';
  }
}

const milestoneTitles: Readonly<Record<string, string>> = {
  DisputeSubmitted: 'Dispute received',
  SpecialistReview: 'Referred to a specialist',
  SpecialistReviewComplete: 'Specialist review completed',
  DebitOrderStartReview: 'Review started', BankChargeStartReview: 'Review started',
  DebitOrderVerifyMandate: 'Payment authorization checked',
  DebitOrderCompleteInvestigation: 'Investigation completed',
  DebitOrderRequestReversal: 'Refund requested', DebitOrderExecuteReversal: 'Refund result received',
  DebitOrderReconcileReversal: 'Refund outcome checked', DebitOrderReject: 'Review decision recorded',
  DebitOrderClose: 'Case closed',
  ChargebackInitiate: 'Card dispute started', ChargebackForwardToAcquirer: 'Sent to the merchant',
  ChargebackMerchantAccept: 'Merchant accepted the dispute',
  ChargebackMerchantContest: 'Merchant challenged the dispute',
  ChargebackReceiveRepresentment: 'Merchant information received',
  ChargebackReviewRepresentment: 'Merchant information reviewed',
  ChargebackApprove: 'Review decision recorded', ChargebackReject: 'Review decision recorded',
  ChargebackRequestSettlement: 'Refund requested', ChargebackExecuteSettlement: 'Refund result received',
  ChargebackClose: 'Case closed',
  BankChargeApprove: 'Review decision recorded', BankChargeReject: 'Review decision recorded',
  BankChargeRequestAdjustment: 'Fee refund requested', BankChargeExecuteAdjustment: 'Fee refund result received',
  BankChargeClose: 'Case closed',
};

@Component({
  selector: 'app-dispute-details',
  imports: [DatePipe, DecimalMoney, RouterLink, Timeline],
  templateUrl: './dispute-details.html',
})
export class DisputeDetails {
  readonly dispute = input.required<Dispute>();
  // Sequence numbers order the timeline; timestamps can tie or arrive out of order.
  protected readonly events = computed<TimelineEvent[]>(() =>
    [...this.dispute().timeline].sort((left, right) => left.sequenceNumber - right.sequenceNumber).map(event => ({
      id: event.id,
      title: milestoneTitles[event.eventType] ?? disputeStatusLabel(event.caseStatus),
      description: event.description,
      occurredAt: event.occurredAt,
    })));
  protected readonly finished = computed(() => this.dispute().caseStatus === 'Closed');
  protected readonly nextStep = computed(() => {
    const dispute = this.dispute();
    if (dispute.caseStatus === 'Closed') return 'This case is closed. You can still review its decision and history here.';
    if (dispute.financialStatus === 'OutcomeUnknown')
      return 'A reviewer needs to confirm the payment outcome. No refund is confirmed yet.';
    if (dispute.financialStatus === 'Failed')
      return 'A reviewer needs to investigate the payment problem. No refund is confirmed.';
    if (dispute.financialStatus === 'Requested')
      return 'We are waiting for confirmation of the payment adjustment. Approval alone does not mean a refund.';
    if (dispute.financialStatus === 'Confirmed' || dispute.decisionStatus === 'Rejected')
      return 'The review outcome is recorded. Your case is awaiting closure.';
    if (dispute.decisionStatus === 'Approved')
      return 'The review is complete. Any payment adjustment must be processed and confirmed separately.';
    if (dispute.chargeback?.status === 'MerchantReview')
      return 'We are waiting for the merchant to respond to the card payment dispute.';
    if (dispute.chargeback?.status === 'MerchantContested')
      return 'The merchant challenged the dispute. Their supporting information is needed before completing the review.';
    if (dispute.chargeback?.status === 'RepresentmentReceived' || dispute.chargeback?.status === 'IssuerReview')
      return 'The merchant information will be reviewed before a decision is made.';
    return dispute.caseStatus === 'ManualReview'
      ? 'A specialist will investigate the transaction and the information you provided.'
      : 'A reviewer will check the transaction and the information you provided.';
  });
  protected readonly typeLabel = transactionTypeLabel;
  protected readonly statusLabel = disputeStatusLabel;
  protected readonly financialLabel = disputeFinancialLabel;
  protected readonly decisionLabel = disputeDecisionLabel;

  protected chargebackLabel(status: ChargebackStatus): string {
    const labels: Record<ChargebackStatus, string> = {
      InitiationPending: 'Starting the card review', Initiated: 'Card review started',
      MerchantReview: 'Waiting for the merchant response',
      MerchantAccepted: 'Merchant accepted the dispute', MerchantContested: 'Merchant challenged the dispute',
      RepresentmentReceived: 'Merchant information received', IssuerReview: 'Reviewing merchant information',
      DecisionIssued: 'Decision recorded', Closed: 'Closed',
    };
    return labels[status];
  }
}
