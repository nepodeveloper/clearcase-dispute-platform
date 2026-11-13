import { Routes } from '@angular/router';
import { TransactionDetailsPage } from './transactions/transaction-details-page';
import { TransactionsPage } from './transactions/transactions-page';
import { DisputesPage } from './disputes/disputes-page';
import { DisputeDetailsPage } from './disputes/dispute-details-page';
import { authGuard, loginGuard } from './auth/auth-guards';
import { LoginPage } from './auth/login-page';
import { WorkspacePage } from './auth/workspace-page';

export const routes: Routes = [
  { path: 'login', component: LoginPage, canActivate: [loginGuard] },
  { path: '', component: TransactionsPage, pathMatch: 'full', canActivate: [authGuard], data: { role: 'Customer' } },
  { path: 'transactions/:id', component: TransactionDetailsPage, canActivate: [authGuard], data: { role: 'Customer' } },
  { path: 'disputes', component: DisputesPage, canActivate: [authGuard], data: { role: 'Customer' } },
  { path: 'disputes/:id', component: DisputeDetailsPage, canActivate: [authGuard], data: { role: 'Customer' } },
  { path: 'agent/disputes', component: WorkspacePage, canActivate: [authGuard], data: { role: 'Agent', title: 'Agent workspace' } },
  { path: 'agent/escalations', component: WorkspacePage, canActivate: [authGuard], data: { role: 'Supervisor', title: 'Supervisor workspace' } },
  { path: 'unauthorized', component: WorkspacePage, canActivate: [authGuard], data: { denied: true } },
  { path: '**', redirectTo: 'unauthorized' },
];
