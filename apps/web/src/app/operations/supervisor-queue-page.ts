import { DatePipe } from '@angular/common';
import { Component, computed, DestroyRef, ElementRef, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { EscalationStatus, SupervisorEscalationPage } from '../supervisor/supervisor-escalations-api';
import { DemoWorkspace } from './demo-workspace';
import { OPERATIONS_API } from './operations-api';
import { DemoHeading, DemoPagination, DemoStat, DemoStatus } from './shared';

interface QueueItem {
  id: string;
  reference: string;
  caseId: string;
  disputeReference: string;
  customerReference: string;
  reason: string;
  status: EscalationStatus;
  createdAt: string;
}

@Component({
  selector: 'cc-demo-supervisor-queue',
  host: { class: 'operations-content' },
  imports: [DatePipe, RouterLink, DemoHeading, DemoPagination, DemoStat, DemoStatus],
  template: `
    <cc-demo-heading title="Escalations queue" eyebrow="Supervisor workspace"
      description="Cases waiting for specialist oversight and a recorded outcome.">
      <span class="info-token">SP &middot; Supervisor</span>
    </cc-demo-heading>
    <div class="stats-grid">
      <cc-demo-stat label="Open escalations" [value]="open()" hint="On current page" />
      <cc-demo-stat label="Acknowledged" [value]="acknowledged()" hint="On current page" />
      <cc-demo-stat label="Resolved" [value]="resolved()" hint="On current page; no money moved" />
    </div>
    <section class="panel" aria-labelledby="supervisor-queue-heading">
      <div class="panel-title"><h2 id="supervisor-queue-heading" tabindex="-1" #listHeading>Needs your attention</h2></div>
      <div class="filter-bar operations-filters">
        <label class="cc-field search-field"><span>Search escalations</span>
          <input class="cc-control" #query type="search" placeholder="Search by reference or customer"
            [value]="search()" (input)="setSearch(query.value)"></label>
        <label class="cc-field"><span>Escalation status</span><select class="cc-control" #state [value]="status()" (change)="setStatus(state.value)">
          @if (operationsApi === null) { <option value="active">Open and acknowledged</option> }
          <option value="">All statuses</option>
          <option>Open</option><option>Acknowledged</option><option>Resolved</option>
        </select></label>
      </div>
      <div class="cc-table-scroll" role="region" aria-label="Escalation queue, scroll horizontally on small screens" tabindex="0">
        <table class="cc-table"><caption class="cc-sr-only">Escalations available for supervisor review</caption>
          <thead><tr><th scope="col">Escalation ID</th><th scope="col">Dispute</th><th scope="col">Customer</th>
            <th scope="col">Reason</th><th scope="col">Status</th><th scope="col">Created</th><th scope="col"><span class="cc-sr-only">Action</span></th></tr></thead>
          <tbody>@for (item of visible(); track item.id) {
            <tr><td><a [routerLink]="['/agent/escalations', item.id]">{{ item.reference }}</a></td>
              <td>{{ item.disputeReference }}</td><td>{{ item.customerReference }}</td>
              <td>{{ item.reason }}</td><td><cc-demo-status [label]="item.status" /></td>
              <td>{{ item.createdAt | date:'dd MMM yyyy':'UTC' }}</td>
              <td><a class="row-action" [routerLink]="['/agent/escalations', item.id]" [attr.aria-label]="'Review ' + item.reference">Review &rarr;</a></td></tr>
          }</tbody>
        </table>
      </div>
      @if (!loading() && filtered().length === 0) {
        <div class="cc-empty" role="status"><h3>No matching escalations</h3><p>There are no escalations matching your search and status filter.</p></div>
      }
      @if (apiError()) { <p class="cc-form-error" role="alert">{{ apiError() }}</p>
        <button class="cc-button cc-button--secondary" type="button" (click)="loadApi()">Retry</button> }
      @if (operationsApi === null) {
        <cc-demo-pagination [page]="page()" [total]="filtered().length" (changed)="changePage($event)" />
      } @else {
        <div class="queue-pagination" aria-label="Escalation pages">
          <button class="cc-button cc-button--secondary" type="button" [disabled]="page() <= 1 || loading()" (click)="previousPage()">Previous</button>
          <span aria-live="polite">Page {{ page() }}</span>
          <button class="cc-button cc-button--secondary" type="button" [disabled]="!apiPage()?.pageInfo?.hasNextPage || loading()" (click)="nextPage()">Next</button>
        </div>
      }
    </section>`,
})
export class DemoSupervisorQueuePage {
  protected readonly workspace = inject(DemoWorkspace, { optional: true });
  protected readonly operationsApi = inject(OPERATIONS_API, { optional: true });
  private readonly destroyRef = inject(DestroyRef);
  private readonly heading = viewChild<ElementRef<HTMLElement>>('listHeading');
  private readonly cursors: (string | null)[] = [null];
  private requestId = 0;
  protected readonly search = signal('');
  protected readonly status = signal(this.operationsApi === null ? 'active' : 'Open');
  protected readonly page = signal(1);
  protected readonly apiPage = signal<SupervisorEscalationPage | null>(null);
  protected readonly loading = signal(false);
  protected readonly apiError = signal('');
  protected readonly demoItems = computed<QueueItem[]>(() => (this.workspace?.escalations() ?? []).map(item => ({
    id: item.id, reference: item.reference, caseId: item.caseId,
    disputeReference: this.workspace?.agentCase(item.caseId)?.reference ?? '',
    customerReference: this.workspace?.agentCase(item.caseId)?.customerName ?? '',
    reason: item.reason, status: item.status, createdAt: item.createdAt,
  })));
  private readonly statItems = computed(() => this.operationsApi === null ? this.demoItems() : this.filtered());
  protected readonly open = computed(() => this.statItems().filter(item => item.status === 'Open').length);
  protected readonly acknowledged = computed(() => this.statItems().filter(item => item.status === 'Acknowledged').length);
  protected readonly resolved = computed(() => this.statItems().filter(item => item.status === 'Resolved').length);
  protected readonly filtered = computed<QueueItem[]>(() => {
    if (this.operationsApi !== null) {
      return (this.apiPage()?.items ?? []).map(item => ({
        id: item.id, reference: item.reference, caseId: item.disputeId,
        disputeReference: item.disputeReference, customerReference: item.customerReference,
        reason: item.reason, status: item.status, createdAt: item.createdAt,
      }));
    }
    return this.demoItems().filter(item =>
      (this.status() === '' || (this.status() === 'active' ? item.status !== 'Resolved' : item.status === this.status())) &&
      `${item.reference} ${item.disputeReference} ${item.customerReference}`.toLowerCase().includes(this.search().trim().toLowerCase()));
  });
  protected readonly visible = computed(() => this.operationsApi === null
    ? this.filtered().slice((this.page() - 1) * 6, this.page() * 6)
    : this.filtered());

  constructor() {
    if (this.operationsApi !== null) this.loadApi();
  }

  protected setSearch(value: string): void {
    this.search.set(value);
    this.resetApiPaging();
  }

  protected setStatus(value: string): void {
    const statuses: readonly EscalationStatus[] = ['Open', 'Acknowledged', 'Resolved'];
    this.status.set(this.operationsApi === null && value === 'active'
      ? 'active'
      : statuses.find(status => status === value) ?? '');
    this.resetApiPaging();
  }

  protected loadApi(): void {
    if (this.operationsApi === null) return;
    const requestId = ++this.requestId;
    this.loading.set(true);
    this.apiError.set('');
    const statuses: readonly EscalationStatus[] = ['Open', 'Acknowledged', 'Resolved'];
    const status = statuses.find(item => item === this.status()) ?? '';
    this.operationsApi.supervisor.list(this.search(), status, this.cursors[this.page() - 1] ?? null)
      .pipe(takeUntilDestroyed(this.destroyRef), finalize(() => {
        if (requestId === this.requestId) this.loading.set(false);
      }))
      .subscribe({
        next: result => { if (requestId === this.requestId) this.apiPage.set(result); },
        error: () => {
          if (requestId === this.requestId) this.apiError.set('Unable to load escalations. Check your Supervisor session and retry.');
        },
      });
  }

  protected previousPage(): void {
    if (this.page() <= 1) return;
    this.page.update(page => page - 1);
    this.loadApi();
    this.heading()?.nativeElement.focus();
  }

  protected nextPage(): void {
    const nextCursor = this.apiPage()?.pageInfo.nextCursor;
    if (nextCursor === undefined) return;
    this.cursors[this.page()] = nextCursor;
    this.page.update(page => page + 1);
    this.loadApi();
    this.heading()?.nativeElement.focus();
  }

  protected changePage(page: number): void { this.page.set(page); this.heading()?.nativeElement.focus(); }

  private resetApiPaging(): void {
    this.page.set(1);
    this.cursors.splice(0, this.cursors.length, null);
    if (this.operationsApi !== null) this.loadApi();
  }
}
