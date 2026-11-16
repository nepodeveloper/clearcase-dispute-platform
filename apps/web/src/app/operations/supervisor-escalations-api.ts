import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';

export type EscalationStatus = 'Open' | 'Acknowledged' | 'Resolved';

export interface SupervisorEscalationListItem {
  id: string;
  reference: string;
  disputeId: string;
  disputeReference: string;
  customerReference: string;
  reason: string;
  status: EscalationStatus;
  createdAt: string;
}

export interface SupervisorEscalationPage {
  items: SupervisorEscalationListItem[];
  pageInfo: { hasNextPage: boolean; nextCursor?: string };
}

export interface SupervisorEscalationHistoryItem {
  id: string;
  action: 'Created' | 'Acknowledged' | 'Resolved';
  employeeId: string;
  privateNote?: string;
  occurredAt: string;
  correlationId: string;
}

export interface SupervisorEscalation {
  id: string;
  reference: string;
  disputeId: string;
  disputeReference: string;
  customerReference: string;
  category: 'CardPurchase' | 'DebitOrder' | 'BankCharge';
  caseStatus: string;
  decisionStatus: string;
  financialStatus: string;
  reason: string;
  note?: string;
  status: EscalationStatus;
  createdByEmployeeId: string;
  createdAt: string;
  history: SupervisorEscalationHistoryItem[];
}

@Injectable({ providedIn: 'root' })
export class SupervisorEscalationsApi {
  private readonly http = inject(HttpClient);

  list(search: string, status: EscalationStatus | '', cursor: string | null) {
    let params = new HttpParams().set('pageSize', 20);
    if (search.trim() !== '') params = params.set('search', search.trim());
    if (status !== '') params = params.set('status', status);
    if (cursor !== null) params = params.set('cursor', cursor);
    return this.http.get<SupervisorEscalationPage>('/api/v1/supervisor/escalations', { params });
  }

  get(id: string) {
    return this.http.get<SupervisorEscalation>(
      `/api/v1/supervisor/escalations/${encodeURIComponent(id)}`,
    );
  }

  acknowledge(id: string, idempotencyKey: string) {
    return this.http.post<SupervisorEscalation>(
      `/api/v1/supervisor/escalations/${encodeURIComponent(id)}/acknowledgements`,
      {},
      { headers: new HttpHeaders({ 'Idempotency-Key': idempotencyKey }) },
    );
  }

  resolve(id: string, note: string, idempotencyKey: string) {
    return this.http.post<SupervisorEscalation>(
      `/api/v1/supervisor/escalations/${encodeURIComponent(id)}/resolutions`,
      { note },
      { headers: new HttpHeaders({ 'Idempotency-Key': idempotencyKey }) },
    );
  }
}
