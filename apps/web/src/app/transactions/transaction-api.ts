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
}
