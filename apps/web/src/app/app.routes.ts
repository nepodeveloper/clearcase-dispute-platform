import { Routes } from '@angular/router';
import { TransactionDetailsPage } from './transactions/transaction-details-page';
import { TransactionsPage } from './transactions/transactions-page';
import { DisputesPage } from './disputes/disputes-page';
import { DisputeDetailsPage } from './disputes/dispute-details-page';
import { authGuard, loginGuard } from './auth/auth-guards';
import { LoginPage } from './auth/login-page';
import { WorkspacePage } from './auth/workspace-page';
import { DemoAgentQueuePage } from './operations/agent-queue-page';
import { DemoInvestigationPage } from './operations/investigation-page';
import { DemoSupervisorQueuePage } from './operations/supervisor-queue-page';
import { DemoSupervisorDetailPage } from './operations/supervisor-detail-page';

export const routes: Routes = [
  { path: 'login', component: LoginPage, canActivate: [loginGuard] },
  { path: '', component: TransactionsPage, pathMatch: 'full', canActivate: [authGuard], data: { role: 'Customer' } },
  { path: 'transactions/:id', component: TransactionDetailsPage, canActivate: [authGuard], data: { role: 'Customer' } },
  { path: 'disputes', component: DisputesPage, canActivate: [authGuard], data: { role: 'Customer' } },
  { path: 'disputes/:id', component: DisputeDetailsPage, canActivate: [authGuard], data: { role: 'Customer' } },
  { path: 'agent/disputes', component: DemoAgentQueuePage, canActivate: [authGuard], data: { role: 'Agent' } },
  { path: 'agent/disputes/:id', component: DemoInvestigationPage, canActivate: [authGuard], data: { role: 'Agent' } },
  { path: 'agent/escalations', component: DemoSupervisorQueuePage, canActivate: [authGuard], data: { role: 'Supervisor' } },
  { path: 'agent/escalations/:id', component: DemoSupervisorDetailPage, canActivate: [authGuard], data: { role: 'Supervisor' } },
  { path: 'unauthorized', component: WorkspacePage, canActivate: [authGuard], data: { denied: true } },
  { path: '**', redirectTo: 'unauthorized' },
];
