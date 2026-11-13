import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
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
  statusDescription: string;
  createdAt: string;
  timeline: DisputeTimelineEvent[];
  chargeback?: Chargeback;
}

export type DisputeSummary = Pick<Dispute, 'id' | 'reference' | 'transactionId' | 'category'
  | 'caseStatus' | 'decisionStatus' | 'financialStatus' | 'createdAt' | 'statusDescription'>;

export interface DisputePage {
  items: DisputeSummary[];
  pageInfo: { hasNextPage: boolean; nextCursor?: string };
}

export interface DisputeQueryError { message: string; retryable: boolean }

export function disputeQueryError(error: unknown, resource: 'dispute' | 'disputes'): DisputeQueryError {
  const status = error instanceof HttpErrorResponse ? error.status : null;
  return {
    message: status === 401 ? `Sign in again to view ${resource}.`
      : status === 403 ? `You do not have permission to view ${resource}.`
        : status === 400 ? 'This dispute link or continuation is invalid. Return to the dispute list.'
          : `Unable to load ${resource}. Please try again.`,
    retryable: status !== null && (status === 0 || status === 429 || status >= 500),
  };
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

  list(cursor?: string) {
    let params = new HttpParams().set('pageSize', 20);
    if (cursor !== undefined) params = params.set('cursor', cursor);
    return this.http.get<DisputePage>('/api/v1/disputes', { params });
  }
}
