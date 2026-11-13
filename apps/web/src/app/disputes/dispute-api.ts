import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { TransactionType } from '../transactions/transaction-api';

export type DisputeCaseStatus = 'Submitted' | 'ManualReview' | 'Closed'
  | 'UnderReview' | 'MandateVerified' | 'ReversalRequested' | 'ReversalConfirmed' | 'Rejected';

export type ChargebackStatus = 'InitiationPending' | 'Initiated' | 'MerchantReview'
  | 'MerchantAccepted' | 'MerchantContested' | 'RepresentmentReceived'
  | 'IssuerReview' | 'DecisionIssued' | 'Closed';

export interface Chargeback {
  reference: string;
  reasonCode: string;
  status: ChargebackStatus;
  lastOperationOutcome: 'Succeeded' | 'Failed' | 'OutcomeUnknown';
  evidence?: { id: string; kind: 'Receipt' | 'DeliveryConfirmation' | 'AuthorizationRecord'; receivedAt: string };
}

export interface CreateDisputeRequest {
  transactionId: string;
  reasonCode: string;
  description: string;
}

export interface DisputeTimelineEvent {
  id: string;
  sequenceNumber: number;
  eventType: string;
  caseStatus: DisputeCaseStatus;
  description: string;
  occurredAt: string;
}

export interface Dispute {
  id: string;
  reference: string;
  transactionId: string;
  category: TransactionType;
  reasonCode: string;
  description: string;
  caseStatus: DisputeCaseStatus;
  decisionStatus: string;
  financialStatus: string;
  createdAt: string;
  timeline: DisputeTimelineEvent[];
  chargeback?: Chargeback;
}

@Injectable({ providedIn: 'root' })
export class DisputeApi {
  private readonly http = inject(HttpClient);

  submit(request: CreateDisputeRequest) {
    return this.http.post<Dispute>('/api/v1/disputes', request);
  }

  get(id: string) {
    return this.http.get<Dispute>(`/api/v1/disputes/${encodeURIComponent(id)}`);
  }
}
