import { Routes } from '@angular/router';
import { authGuard, loginGuard } from './auth/auth-guards';

export const routes: Routes = [
  { path: 'login', title: 'Sign in | ClearCase', loadComponent: () => import('./auth/login-page').then(m => m.LoginPage), canActivate: [loginGuard],
    data: { label: 'Sign in' } },
  { path: '', title: 'Transactions | ClearCase', loadComponent: () => import('./transactions/transactions-page').then(m => m.TransactionsPage), pathMatch: 'full',
    canActivate: [authGuard], data: { role: 'Customer', label: 'Transactions', section: 'transactions' } },
  { path: 'transactions/:id', title: 'Transaction details | ClearCase', loadComponent: () => import('./transactions/transaction-details-page').then(m => m.TransactionDetailsPage),
    canActivate: [authGuard], data: { role: 'Customer', label: 'Transaction details', section: 'transactions' } },
  { path: 'disputes', title: 'My disputes | ClearCase', loadComponent: () => import('./disputes/disputes-page').then(m => m.DisputesPage),
    canActivate: [authGuard], data: { role: 'Customer', label: 'My disputes', section: 'disputes' } },
  { path: 'disputes/:id', title: 'Dispute timeline | ClearCase', loadComponent: () => import('./disputes/dispute-details-page').then(m => m.DisputeDetailsPage),
    canActivate: [authGuard], data: { role: 'Customer', label: 'Dispute timeline', section: 'disputes' } },
  { path: 'notifications', title: 'Notifications | ClearCase', loadComponent: () => import('./notifications/notifications-page').then(m => m.NotificationsPage),
    canActivate: [authGuard], data: { role: 'Customer', label: 'Notifications', section: 'notifications' } },
  { path: 'agent/disputes', title: 'Agent work queue | ClearCase', loadComponent: () => import('./operations/agent-queue-page').then(m => m.AgentQueuePage),
    canActivate: [authGuard], data: { role: 'Agent', label: 'Agent work queue', section: 'agent' } },
  { path: 'agent/disputes/:id', title: 'Case investigation | ClearCase', loadComponent: () => import('./operations/investigation-page').then(m => m.InvestigationPage),
    canActivate: [authGuard], data: { role: 'Agent', label: 'Case investigation', section: 'agent' } },
  { path: 'supervisor/escalations', title: 'Supervisor queue | ClearCase', loadComponent: () => import('./operations/supervisor-queue-page').then(m => m.SupervisorQueuePage),
    canActivate: [authGuard], data: { role: 'Supervisor', label: 'Supervisor queue', section: 'supervisor' } },
  { path: 'supervisor/escalations/:id', title: 'Supervisor review | ClearCase', loadComponent: () => import('./operations/supervisor-detail-page').then(m => m.SupervisorDetailPage),
    canActivate: [authGuard], data: { role: 'Supervisor', label: 'Supervisor review', section: 'supervisor' } },
  // Legacy Supervisor URLs kept so existing bookmarks and links keep working.
  { path: 'agent/escalations', redirectTo: 'supervisor/escalations', pathMatch: 'full' },
  { path: 'agent/escalations/:id', redirectTo: 'supervisor/escalations/:id' },
  { path: 'unauthorized', title: 'Access denied | ClearCase', loadComponent: () => import('./auth/workspace-page').then(m => m.WorkspacePage), canActivate: [authGuard],
    data: { denied: true, label: 'Access denied' } },
  { path: '**', title: 'Page not found | ClearCase', loadComponent: () => import('./not-found-page').then(m => m.NotFoundPage), data: { label: 'Page not found' } },
];
