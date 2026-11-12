import { Routes } from '@angular/router';
import { TransactionDetailsPage } from './transactions/transaction-details-page';
import { TransactionsPage } from './transactions/transactions-page';

export const routes: Routes = [
  { path: '', component: TransactionsPage, pathMatch: 'full' },
  { path: 'transactions/:id', component: TransactionDetailsPage },
];
