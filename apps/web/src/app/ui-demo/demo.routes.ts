import { Component, inject, input } from '@angular/core';
import { CanActivateFn, Router, RouterLink, Routes } from '@angular/router';
import { DemoSession } from './demo-session';

const requireDemoRole: CanActivateFn = route => {
  const role = inject(DemoSession).role();
  const router = inject(Router);
  if (role === null) return router.createUrlTree(['/login']);
  return role === route.data['role'] || router.createUrlTree(['/unavailable']);
};

@Component({
  imports: [RouterLink],
  template: `<section class="cc-empty"><h1 tabindex="-1">{{ heading() }}</h1>
    <p>{{ description() }}</p><a class="cc-button cc-button--secondary" routerLink="/login">Choose a demo journey</a></section>`,
})
class DemoUnavailablePage {
  readonly heading = input('Page not found');
  readonly description = input('This link does not match a screen in the UI demo.');
}

export const demoRoutes: Routes = [
  { path: 'login', title: 'Login | ClearCase UI demo', data: { label: 'Login', section: 'login' },
    loadComponent: () => import('./login-page').then(m => m.DemoLoginPage) },
  { path: '', pathMatch: 'full', title: 'Transactions | ClearCase UI demo', canActivate: [requireDemoRole],
    data: { role: 'customer', label: 'Transactions', section: 'transactions' },
    loadComponent: () => import('./transactions-page').then(m => m.DemoTransactionsPage) },
  { path: 'transactions/:id', title: 'Transaction details | ClearCase UI demo', canActivate: [requireDemoRole],
    data: { role: 'customer', label: 'Transaction details & dispute', section: 'transaction' },
    loadComponent: () => import('./transaction-page').then(m => m.DemoTransactionPage) },
  { path: 'disputes', title: 'My disputes | ClearCase UI demo', canActivate: [requireDemoRole],
    data: { role: 'customer', label: 'My disputes', section: 'disputes' },
    loadComponent: () => import('./disputes-page').then(m => m.DemoDisputesPage) },
  { path: 'disputes/:id', title: 'Dispute timeline | ClearCase UI demo', canActivate: [requireDemoRole],
    data: { role: 'customer', label: 'Dispute timeline', section: 'timeline' },
    loadComponent: () => import('./timeline-page').then(m => m.DemoTimelinePage) },
  { path: 'agent/disputes', title: 'Agent queue | ClearCase UI demo', canActivate: [requireDemoRole],
    data: { role: 'agent', label: 'Agent work queue', section: 'agent-queue' },
    loadComponent: () => import('./agent-queue-page').then(m => m.DemoAgentQueuePage) },
  { path: 'agent/disputes/:id/escalate', title: 'Escalate case | ClearCase UI demo', canActivate: [requireDemoRole],
    data: { role: 'agent', label: 'Escalate case', section: 'escalate' },
    loadComponent: () => import('./escalation-page').then(m => m.DemoEscalationPage) },
  { path: 'agent/disputes/:id', title: 'Case investigation | ClearCase UI demo', canActivate: [requireDemoRole],
    data: { role: 'agent', label: 'Case investigation', section: 'investigation' },
    loadComponent: () => import('./investigation-page').then(m => m.DemoInvestigationPage) },
  { path: 'agent/escalations', title: 'Supervisor queue | ClearCase UI demo', canActivate: [requireDemoRole],
    data: { role: 'supervisor', label: 'Supervisor escalation queue', section: 'supervisor' },
    loadComponent: () => import('./supervisor-queue-page').then(m => m.DemoSupervisorQueuePage) },
  { path: 'agent/escalations/:id', title: 'Supervisor review | ClearCase UI demo', canActivate: [requireDemoRole],
    data: { role: 'supervisor', label: 'Supervisor escalation details', section: 'supervisor-detail' },
    loadComponent: () => import('./supervisor-detail-page').then(m => m.DemoSupervisorDetailPage) },
  { path: 'unavailable', title: 'Journey unavailable | ClearCase UI demo', component: DemoUnavailablePage,
    data: { label: 'Journey unavailable', heading: 'Choose the matching demo role',
      description: 'This screen is not available in your current journey. Return to the role picker to preview it.' } },
  { path: '**', title: 'Page not found | ClearCase UI demo',
    data: { label: 'Page not found', heading: 'Page not found',
      description: 'This link does not match a screen in the UI demo.' }, component: DemoUnavailablePage },
];
