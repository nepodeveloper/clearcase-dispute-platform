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
  activeDisputeId?: string;
}

export interface TransactionPageInfo {
  hasNextPage: boolean;
  nextCursor?: string;
}

export interface TransactionPage {
  items: Transaction[];
  pageInfo: TransactionPageInfo;
  // Legacy page-number fields are still returned; the UI pages with pageInfo.nextCursor only.
  totalCount?: number;
  pageNumber?: number;
  pageSize?: number;
  totalPages?: number;
  hasNextPage?: boolean;
}

export interface TransactionDetails extends Transaction {
  merchantName?: string;
  reference?: string;
}

export type DisputeEligibilityOutcome =
  | 'EligibleToSubmit'
  | 'RequiresManualReview'
  | 'Ineligible';

export interface DisputeReason {
  code: string;
  label: string;
}

export interface TransactionDisputeEligibility {
  transactionId: string;
  outcome: DisputeEligibilityOutcome;
  reasonCodes: string[];
  explanation: string;
  policyVersion: string;
  evaluatedAt: string;
  availableReasons: DisputeReason[];
  ordinaryReversalOutcome?: DisputeEligibilityOutcome;
  reviewRoute?: 'Investigation';
}

export interface TransactionQuery {
  pageSize: number;
  /** Opaque continuation from the previous page's pageInfo.nextCursor; omitted for the first page. */
  cursor?: string;
  transactionType: TransactionType | '';
  fromDate: string;
  toDate: string;
  search: string;
}

@Injectable({ providedIn: 'root' })
export class TransactionApi {
  private readonly http = inject(HttpClient);

  list(query: TransactionQuery) {
    // The server rejects a cursor combined with pageNumber, so the first page is the only one that names a page.
    let params = new HttpParams().set('pageSize', query.pageSize);
    params = query.cursor === undefined ? params.set('pageNumber', 1) : params.set('cursor', query.cursor);
    if (query.transactionType !== '') params = params.set('transactionType', query.transactionType);
    if (query.fromDate !== '') params = params.set('fromDate', query.fromDate);
    if (query.toDate !== '') params = params.set('toDate', query.toDate);
    if (query.search !== '') params = params.set('search', query.search);
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
