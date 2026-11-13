import { Routes } from '@angular/router';
import { TransactionDetailsPage } from './transactions/transaction-details-page';
import { TransactionsPage } from './transactions/transactions-page';
import { DisputesPage } from './disputes/disputes-page';
import { DisputeDetailsPage } from './disputes/dispute-details-page';

export const routes: Routes = [
  { path: '', component: TransactionsPage, pathMatch: 'full' },
  { path: 'transactions/:id', component: TransactionDetailsPage },
  { path: 'disputes', component: DisputesPage },
  { path: 'disputes/:id', component: DisputeDetailsPage },
];
