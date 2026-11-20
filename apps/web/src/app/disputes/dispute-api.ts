import { HttpClient, HttpErrorResponse, HttpHeaders, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { TransactionDetails, TransactionType } from '../transactions/transaction-api';

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
  decisionStatus: 'Pending' | 'Approved' | 'Rejected';
  financialStatus: 'NotInitiated' | 'Requested' | 'OutcomeUnknown' | 'Failed' | 'Confirmed';
  statusDescription: string;
  createdAt: string;
  /** Optimistic concurrency version sent back as expectedVersion on customer commands. */
  version: number;
  /** Computed by the server and omitted once the case is Closed or Rejected. */
  expectedResolutionBy?: string;
  /** Server decision; the client never re-derives whether a withdrawal is allowed. */
  canWithdraw?: boolean;
  timeline: DisputeTimelineEvent[];
  chargeback?: Chargeback;
  transaction?: Pick<TransactionDetails, 'description' | 'merchantName' | 'amount' | 'currency' | 'transactionDate'>;
}

export interface CustomerEvidence {
  id: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  uploadedAt: string;
  note?: string;
}

export interface CustomerEvidenceList { items: CustomerEvidence[] }

/** Reads the stable `code` of an application/problem+json error response, if there is one. */
export function problemCode(error: unknown): string | null {
  const body: unknown = error instanceof HttpErrorResponse ? error.error : null;
  return typeof body === 'object' && body !== null && 'code' in body && typeof body.code === 'string' ? body.code : null;
}

export type DisputeSummary = Pick<Dispute, 'id' | 'reference' | 'transactionId' | 'category'
  | 'caseStatus' | 'decisionStatus' | 'financialStatus' | 'createdAt' | 'statusDescription' | 'transaction'>;

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

  submit(request: CreateDisputeRequest, idempotencyKey: string) {
    return this.http.post<Dispute>('/api/v1/disputes', request, {
      headers: new HttpHeaders({ 'Idempotency-Key': idempotencyKey }),
    });
  }

  get(id: string) {
    return this.http.get<Dispute>(`/api/v1/disputes/${encodeURIComponent(id)}`);
  }

  list(cursor?: string) {
    let params = new HttpParams().set('pageSize', 20);
    if (cursor !== undefined) params = params.set('cursor', cursor);
    return this.http.get<DisputePage>('/api/v1/disputes', { params });
  }

  withdraw(id: string, expectedVersion: number, reason?: string) {
    return this.http.post<Dispute>(`/api/v1/disputes/${encodeURIComponent(id)}/withdrawals`,
      { expectedVersion, ...(reason === undefined || reason === '' ? {} : { reason }) });
  }

  listEvidence(id: string) {
    return this.http.get<CustomerEvidenceList>(`/api/v1/disputes/${encodeURIComponent(id)}/evidence`);
  }

  /** Multipart upload; the browser sets the Content-Type and boundary. Emits progress events. */
  uploadEvidence(id: string, file: File, note: string) {
    const body = new FormData();
    body.append('file', file, file.name);
    if (note !== '') body.append('note', note);
    return this.http.post<CustomerEvidence>(`/api/v1/disputes/${encodeURIComponent(id)}/evidence`, body,
      { observe: 'events', reportProgress: true });
  }

  /** Same-origin URL that downloads with the session cookie; the server answers with Content-Disposition: attachment. */
  evidenceContentUrl(id: string, evidenceId: string): string {
    return `/api/v1/disputes/${encodeURIComponent(id)}/evidence/${encodeURIComponent(evidenceId)}/content`;
  }
}
