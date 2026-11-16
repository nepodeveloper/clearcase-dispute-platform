import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';

export type AgentDisputeCategory = 'CardPurchase' | 'DebitOrder' | 'BankCharge';
export type AgentCaseStatus =
  | 'Submitted'
  | 'ManualReview'
  | 'Closed'
  | 'UnderReview'
  | 'MandateVerified'
  | 'ReversalRequested'
  | 'ReversalConfirmed'
  | 'Rejected';
export type AgentDecisionStatus = 'Pending' | 'Approved' | 'Rejected';
export type AgentFinancialStatus = 'NotInitiated' | 'Requested' | 'Confirmed' | 'Failed' | 'OutcomeUnknown';

export function agentCaseStatusLabel(status: AgentCaseStatus): string {
  const labels: Record<AgentCaseStatus, string> = {
    Submitted: 'Submitted',
    ManualReview: 'Specialist review',
    Closed: 'Closed',
    UnderReview: 'Under review',
    MandateVerified: 'Mandate verified',
    ReversalRequested: 'Reversal requested',
    ReversalConfirmed: 'Reversal confirmed',
    Rejected: 'Rejected',
  };
  return labels[status];
}

export interface AgentDisputeListItem {
  id: string;
  reference: string;
  customerReference: string;
  category: AgentDisputeCategory;
  transactionDescription: string;
  amountMinorUnits: string;
  currency: string;
  reasonCode: string;
  caseStatus: AgentCaseStatus;
  decisionStatus: AgentDecisionStatus;
  financialStatus: AgentFinancialStatus;
  createdAt: string;
  lastActivityAt: string;
  version: number;
}

export interface AgentDisputePage {
  items: AgentDisputeListItem[];
  pageInfo: { hasNextPage: boolean; nextCursor?: string };
}

export interface AgentInvestigationNote {
  id: string;
  disputeId: string;
  employeeId: string;
  text: string;
  createdAt: string;
  correlationId: string;
}

export interface AgentTimelineEvent {
  id: string;
  sequenceNumber: number;
  eventType: string;
  caseStatus: AgentCaseStatus;
  description: string;
  occurredAt: string;
}

export interface AgentActiveEscalation {
  id: string;
  reference: string;
  reason: string;
  status: 'Open' | 'Acknowledged';
  createdAt: string;
}

export interface AgentDisputeDetails {
  id: string;
  reference: string;
  customerReference: string;
  category: AgentDisputeCategory;
  reasonCode: string;
  customerStatement: string;
  eligibilityOutcome: string;
  eligibilityPolicyVersion: string;
  caseStatus: AgentCaseStatus;
  decisionStatus: AgentDecisionStatus;
  financialStatus: AgentFinancialStatus;
  createdAt: string;
  version: number;
  transaction: {
    description: string;
    amountMinorUnits: string;
    currency: string;
    transactionDate: string;
    mandateType?: string;
    collectionDate?: string;
    mandateAuthorised?: boolean;
    mandateMatchesCollection?: boolean;
  };
  reversalOperationReference?: string;
  bankChargeAdjustmentReference?: string;
  cardWorkflow?: {
    reference: string;
    reasonCode: string;
    status: string;
    lastOperationOutcome: string;
  };
  activeEscalation?: AgentActiveEscalation;
  timeline: AgentTimelineEvent[];
  notes: AgentInvestigationNote[];
}

export interface AgentDisputeFilters {
  search: string;
  category: AgentDisputeCategory | '';
  caseStatus: AgentCaseStatus | '';
}

@Injectable({ providedIn: 'root' })
export class AgentDisputesApi {
  private readonly http = inject(HttpClient);

  list(filters: AgentDisputeFilters, cursor: string | null) {
    let params = new HttpParams().set('pageSize', 20);
    if (filters.search.trim() !== '') params = params.set('search', filters.search.trim());
    if (filters.category !== '') params = params.set('category', filters.category);
    if (filters.caseStatus !== '') params = params.set('caseStatus', filters.caseStatus);
    if (cursor !== null) params = params.set('cursor', cursor);
    return this.http.get<AgentDisputePage>('/api/v1/agent/disputes', { params });
  }

  get(id: string) {
    return this.http.get<AgentDisputeDetails>(
      `/api/v1/agent/disputes/${encodeURIComponent(id)}`,
    );
  }

  listNotes(id: string) {
    return this.http.get<AgentInvestigationNote[]>(
      `/api/v1/agent/disputes/${encodeURIComponent(id)}/notes`,
    );
  }

  createNote(id: string, text: string, idempotencyKey: string) {
    return this.http.post<AgentInvestigationNote>(
      `/api/v1/agent/disputes/${encodeURIComponent(id)}/notes`,
      { text },
      { headers: new HttpHeaders({ 'Idempotency-Key': idempotencyKey }) },
    );
  }

  createEscalation(id: string, reason: string, note: string, idempotencyKey: string) {
    return this.http.post<AgentActiveEscalation>(
      `/api/v1/agent/disputes/${encodeURIComponent(id)}/escalations`,
      { reason, note },
      { headers: new HttpHeaders({ 'Idempotency-Key': idempotencyKey }) },
    );
  }
}

export function decimalFromMinorUnits(amountMinorUnits: string): string {
  if (!/^-?\d+$/.test(amountMinorUnits)) {
    throw new Error('Expected integer minor units.');
  }
  const amount = BigInt(amountMinorUnits);
  const negative = amount < 0n;
  const absolute = negative ? -amount : amount;
  const whole = (absolute / 100n).toString();
  const fraction = (absolute % 100n).toString().padStart(2, '0');
  return `${negative ? '-' : ''}${whole}.${fraction}`;
}
