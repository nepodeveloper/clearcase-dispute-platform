import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';

export type TransactionType = 'CardPurchase' | 'DebitOrder' | 'BankCharge';

export interface Transaction {
  id: string;
  transactionType: TransactionType;
  description: string;
  amount: string;
  currency: string;
  transactionDate: string;
  status: string;
  isDisputable: boolean;
}

export interface TransactionPage {
  items: Transaction[];
  pageNumber: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasNextPage: boolean;
}

export interface TransactionDetails extends Transaction {
  merchantName?: string;
  reference?: string;
}

export type DisputeEligibilityOutcome =
  | 'EligibleToSubmit'
  | 'RequiresManualReview'
  | 'Ineligible';

export interface TransactionDisputeEligibility {
  transactionId: string;
  outcome: DisputeEligibilityOutcome;
  reasonCodes: string[];
  explanation: string;
  policyVersion: string;
  evaluatedAt: string;
}

export interface TransactionQuery {
  pageNumber: number;
  pageSize: number;
  transactionType: TransactionType | '';
  fromDate: string;
  toDate: string;
}

@Injectable({ providedIn: 'root' })
export class TransactionApi {
  private readonly http = inject(HttpClient);

  list(query: TransactionQuery) {
    let params = new HttpParams()
      .set('pageNumber', query.pageNumber)
      .set('pageSize', query.pageSize);
    if (query.transactionType !== '') params = params.set('transactionType', query.transactionType);
    if (query.fromDate !== '') params = params.set('fromDate', query.fromDate);
    if (query.toDate !== '') params = params.set('toDate', query.toDate);
    return this.http.get<TransactionPage>('/api/v1/transactions', { params });
  }

  getDetails(id: string) {
    return this.http.get<TransactionDetails>(`/api/v1/transactions/${encodeURIComponent(id)}`);
  }

  getDisputeEligibility(id: string) {
    return this.http.get<TransactionDisputeEligibility>(
      `/api/v1/transactions/${encodeURIComponent(id)}/dispute-eligibility`,
    );
  }
}
