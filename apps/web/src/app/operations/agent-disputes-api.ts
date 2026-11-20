import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { CustomerEvidenceList } from '../disputes/dispute-api';

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
export type EvidenceKind = 'Receipt' | 'DeliveryConfirmation' | 'AuthorizationRecord';
/** Computed by the server from the case age; `Complete` once the case is Closed or Rejected. */
export type SlaStatus = 'OnTrack' | 'DueSoon' | 'Breached' | 'Complete';
export type AssignmentFilter = 'all' | 'mine' | 'unassigned';
export type FinancialOperationStatus = 'Pending' | 'InFlight' | 'Succeeded' | 'Failed' | 'Unknown';

export interface AgentAssignee {
  employeeId: string;
  displayName: string;
}

/** Read-only view of a durable financial operation; payloads are never exposed. */
export interface AgentFinancialOperation {
  kind: string;
  operationReference: string;
  status: FinancialOperationStatus;
  updatedAt: string;
}

export function slaStatusLabel(status: SlaStatus): string | null {
  switch (status) {
    case 'OnTrack': return 'On track';
    case 'DueSoon': return 'Due soon';
    case 'Breached': return 'Breached';
    case 'Complete': return null;
  }
}

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
  slaDueAt: string;
  slaStatus: SlaStatus;
  assignedTo?: AgentAssignee;
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

/** Safe creation and idempotent replay payload; never contains Supervisor audit history. */
export interface AgentEscalationCreation {
  id: string;
  reference: string;
  disputeId: string;
  reason: string;
  note?: string;
  status: 'Open' | 'Acknowledged' | 'Resolved';
  createdAt: string;
}

/** Staff command the server currently permits for the case; the client never derives workflow rules. */
export interface AgentAvailableAction {
  command: string;
  label: string;
  description: string;
  movesMoney: boolean;
  terminal: boolean;
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
  slaDueAt: string;
  slaStatus: SlaStatus;
  assignedTo?: AgentAssignee;
  financialOperations: AgentFinancialOperation[];
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
  availableActions: AgentAvailableAction[];
  timeline: AgentTimelineEvent[];
  notes: AgentInvestigationNote[];
}

export interface AgentDisputeFilters {
  search: string;
  category: AgentDisputeCategory | '';
  caseStatus: AgentCaseStatus | '';
  assignment: AssignmentFilter;
}

@Injectable({ providedIn: 'root' })
export class AgentDisputesApi {
  private readonly http = inject(HttpClient);

  list(filters: AgentDisputeFilters, cursor: string | null, pageSize: number) {
    let params = new HttpParams().set('pageSize', pageSize);
    if (filters.search.trim() !== '') params = params.set('search', filters.search.trim());
    if (filters.category !== '') params = params.set('category', filters.category);
    if (filters.caseStatus !== '') params = params.set('caseStatus', filters.caseStatus);
    if (filters.assignment !== 'all') params = params.set('assignment', filters.assignment);
    if (cursor !== null) params = params.set('cursor', cursor);
    return this.http.get<AgentDisputePage>('/api/v1/agent/disputes', { params });
  }

  get(id: string) {
    return this.http.get<AgentDisputeDetails>(
      `/api/v1/agent/disputes/${encodeURIComponent(id)}`,
    );
  }

  /** Workload assignment only: it never changes the case state or its version. */
  claim(id: string) {
    return this.http.post<AgentDisputeDetails>(`/api/v1/agent/disputes/${encodeURIComponent(id)}/assignment`, {});
  }

  release(id: string) {
    return this.http.delete<AgentDisputeDetails>(`/api/v1/agent/disputes/${encodeURIComponent(id)}/assignment`);
  }

  listEvidence(id: string) {
    return this.http.get<CustomerEvidenceList>(`/api/v1/agent/disputes/${encodeURIComponent(id)}/evidence`);
  }

  evidenceContentUrl(id: string, evidenceId: string): string {
    return `/api/v1/agent/disputes/${encodeURIComponent(id)}/evidence/${encodeURIComponent(evidenceId)}/content`;
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

  /** Server checks bank-employee role, CSRF, expectedVersion and Domain transition rules. */
  command(id: string, command: string, expectedVersion: number,
    evidence?: { id: string; kind: EvidenceKind }) {
    return this.http.post<unknown>(
      `/api/v1/agent/disputes/${encodeURIComponent(id)}/commands`,
      { command, expectedVersion, ...(evidence ? { evidence } : {}) });
  }

  createEscalation(id: string, reason: string, note: string, idempotencyKey: string) {
    return this.http.post<AgentEscalationCreation>(
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
