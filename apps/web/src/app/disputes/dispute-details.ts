import { DatePipe } from '@angular/common';
import { Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TransactionType } from '../transactions/transaction-api';
import { transactionTypeLabel } from '../transactions/transaction-type-label';
import { Timeline, TimelineEvent } from '../ui/ui-components';
import { ChargebackStatus, Dispute, DisputeCaseStatus } from './dispute-api';

export function disputeStatusLabel(status: DisputeCaseStatus): string {
  const labels: Record<DisputeCaseStatus, string> = {
    Submitted: 'Submitted - awaiting review',
    ManualReview: 'Referred for specialist review',
    Closed: 'Closed',
    UnderReview: 'Under review',
    MandateVerified: 'Mandate verified',
    ReversalRequested: 'Reversal requested',
    ReversalConfirmed: 'Simulated reversal confirmed',
    Rejected: 'Rejected',
  };
  return labels[status];
}

export function disputeFinancialLabel(status: string, category: TransactionType): string {
  switch (status) {
    case 'NotInitiated': return 'Not initiated';
    case 'Requested': return 'Requested - not confirmed';
    case 'OutcomeUnknown': return 'Outcome unknown - not confirmed';
    case 'Failed': return 'Failed - no financial adjustment confirmed';
    case 'Confirmed': return category === 'CardPurchase'
      ? 'Simulated settlement confirmed - no real money moved'
      : category === 'BankCharge'
        ? 'Simulated fee adjustment confirmed - no real money moved'
        : 'Simulated reversal confirmed - no real money moved';
    default: return status;
  }
}

@Component({
  selector: 'app-dispute-details',
  imports: [DatePipe, RouterLink, Timeline],
  templateUrl: './dispute-details.html',
})
export class DisputeDetails {
  readonly dispute = input.required<Dispute>();
  // Sequence numbers order the timeline; timestamps can tie or arrive out of order.
  protected readonly events = computed<TimelineEvent[]>(() =>
    [...this.dispute().timeline].sort((left, right) => left.sequenceNumber - right.sequenceNumber).map(event => ({
      id: event.id,
      title: disputeStatusLabel(event.caseStatus),
      description: event.description,
      occurredAt: event.occurredAt,
    })));
  protected readonly finished = computed(() =>
    ['Closed', 'Rejected', 'ReversalConfirmed'].includes(this.dispute().caseStatus));
  protected readonly typeLabel = transactionTypeLabel;
  protected readonly statusLabel = disputeStatusLabel;
  protected readonly financialLabel = disputeFinancialLabel;

  protected chargebackLabel(status: ChargebackStatus): string {
    const labels: Record<ChargebackStatus, string> = {
      InitiationPending: 'Initiation pending', Initiated: 'Initiated', MerchantReview: 'Merchant review',
      MerchantAccepted: 'Merchant accepted', MerchantContested: 'Merchant contested',
      RepresentmentReceived: 'Representment received', IssuerReview: 'Issuer review',
      DecisionIssued: 'Decision issued', Closed: 'Closed',
    };
    return labels[status];
  }
}
