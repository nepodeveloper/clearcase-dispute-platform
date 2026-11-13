import { DatePipe } from '@angular/common';
import { Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TransactionType } from '../transactions/transaction-api';
import { transactionTypeLabel } from '../transactions/transaction-type-label';
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

@Component({
  selector: 'app-dispute-details',
  imports: [DatePipe, RouterLink],
  templateUrl: './dispute-details.html',
  styleUrl: './dispute-tracking.css',
})
export class DisputeDetails {
  readonly dispute = input.required<Dispute>();
  protected readonly typeLabel = transactionTypeLabel;
  protected readonly statusLabel = disputeStatusLabel;

  protected financialLabel(status: string, category: TransactionType): string {
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
