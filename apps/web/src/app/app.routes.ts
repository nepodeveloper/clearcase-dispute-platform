import { Routes } from '@angular/router';
import { TransactionDetailsPage } from './transactions/transaction-details-page';
import { TransactionsPage } from './transactions/transactions-page';
import { DisputesPage } from './disputes/disputes-page';
import { DisputeDetailsPage } from './disputes/dispute-details-page';
import { authGuard, loginGuard } from './auth/auth-guards';
import { LoginPage } from './auth/login-page';
import { WorkspacePage } from './auth/workspace-page';
import { AgentQueuePage } from './operations/agent-queue-page';
import { InvestigationPage } from './operations/investigation-page';
import { SupervisorQueuePage } from './operations/supervisor-queue-page';
import { SupervisorDetailPage } from './operations/supervisor-detail-page';

export const routes: Routes = [
  { path: 'login', title: 'Sign in | ClearCase', component: LoginPage, canActivate: [loginGuard],
    data: { label: 'Sign in' } },
  { path: '', title: 'Transactions | ClearCase', component: TransactionsPage, pathMatch: 'full',
    canActivate: [authGuard], data: { role: 'Customer', label: 'Transactions', section: 'transactions' } },
  { path: 'transactions/:id', title: 'Transaction details | ClearCase', component: TransactionDetailsPage,
    canActivate: [authGuard], data: { role: 'Customer', label: 'Transaction details', section: 'transactions' } },
  { path: 'disputes', title: 'My disputes | ClearCase', component: DisputesPage,
    canActivate: [authGuard], data: { role: 'Customer', label: 'My disputes', section: 'disputes' } },
  { path: 'disputes/:id', title: 'Dispute timeline | ClearCase', component: DisputeDetailsPage,
    canActivate: [authGuard], data: { role: 'Customer', label: 'Dispute timeline', section: 'disputes' } },
  { path: 'agent/disputes', title: 'Agent work queue | ClearCase', component: AgentQueuePage,
    canActivate: [authGuard], data: { role: 'Agent', label: 'Agent work queue', section: 'agent' } },
  { path: 'agent/disputes/:id', title: 'Case investigation | ClearCase', component: InvestigationPage,
    canActivate: [authGuard], data: { role: 'Agent', label: 'Case investigation', section: 'agent' } },
  { path: 'agent/escalations', title: 'Supervisor queue | ClearCase', component: SupervisorQueuePage,
    canActivate: [authGuard], data: { role: 'Supervisor', label: 'Supervisor queue', section: 'supervisor' } },
  { path: 'agent/escalations/:id', title: 'Supervisor review | ClearCase', component: SupervisorDetailPage,
    canActivate: [authGuard], data: { role: 'Supervisor', label: 'Supervisor review', section: 'supervisor' } },
  { path: 'unauthorized', title: 'Access denied | ClearCase', component: WorkspacePage, canActivate: [authGuard],
    data: { denied: true, label: 'Access denied' } },
  { path: '**', redirectTo: 'unauthorized' },
];
