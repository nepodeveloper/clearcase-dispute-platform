import { DatePipe } from '@angular/common';
import { Component, computed, DestroyRef, ElementRef, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { createFilterDebouncer } from '../ui/filter-debounce';
import { defaultPageSize, PageHeading, PageSize, PageSizeSelect, StatCard, StatusBadge } from '../ui/ui-components';
import { EscalationStatus, SupervisorEscalationPage, SupervisorEscalationsApi } from './supervisor-escalations-api';

const escalationStatuses: readonly EscalationStatus[] = ['Open', 'Acknowledged', 'Resolved'];

@Component({
  selector: 'cc-supervisor-queue',
  imports: [DatePipe, RouterLink, PageHeading, PageSizeSelect, StatCard, StatusBadge],
  template: `
    <cc-page-heading title="Escalations queue" eyebrow="Supervisor workspace"
      description="Cases waiting for specialist oversight and a recorded outcome.">
      <span class="info-token">SP &middot; Supervisor</span>
    </cc-page-heading>
    <div class="stats-grid">
      <cc-stat label="Open escalations" [value]="open()" hint="On current page" />
      <cc-stat label="Acknowledged" [value]="acknowledged()" hint="On current page" />
      <cc-stat label="Resolved" [value]="resolved()" hint="On current page; no money moved" />
    </div>
    <section class="panel" aria-labelledby="supervisor-queue-heading" [attr.aria-busy]="loading()">
      <div class="panel-title"><h2 id="supervisor-queue-heading" tabindex="-1" #listHeading>Needs your attention</h2>
        <span class="muted">Persisted escalations</span></div>
      <div class="filter-bar operations-filters">
        <label class="cc-field search-field"><span>Search escalations</span>
          <input class="cc-control" #query type="search" placeholder="Search by reference or customer"
            [value]="search()" (input)="setSearch(query.value)" (keydown.enter)="applyNow()"></label>
        <label class="cc-field"><span>Escalation status</span>
          <select class="cc-control" #state [value]="status()" (change)="setStatus(state.value)">
            <option value="">All statuses</option>
            <option>Open</option><option>Acknowledged</option><option>Resolved</option>
          </select></label>
      </div>
      <p class="muted filter-hint">Results update as you type or choose a filter.</p>
      @if (error()) {
        <div class="cc-form-error" role="alert"><p>{{ error() }}</p>
          <button class="cc-button cc-button--secondary" type="button" (click)="load()">Retry</button>
        </div>
      }
      @if (loading()) { <p class="muted" role="status">Loading persisted escalations...</p> }
      <div class="cc-table-scroll" role="region" aria-label="Escalation queue, scroll horizontally on small screens" tabindex="0">
        <table class="cc-table"><caption class="cc-sr-only">Escalations available for supervisor review</caption>
          <thead><tr><th scope="col">Escalation ID</th><th scope="col">Dispute</th><th scope="col">Customer</th>
            <th scope="col">Reason</th><th scope="col">Status</th><th scope="col">Created</th><th scope="col"><span class="cc-sr-only">Action</span></th></tr></thead>
          <tbody>@for (item of items(); track item.id) {
            <tr><td><a [routerLink]="['/supervisor/escalations', item.id]">{{ item.reference }}</a></td>
              <td>{{ item.disputeReference }}</td><td>{{ item.customerReference }}</td>
              <td>{{ item.reason }}</td><td><cc-status-badge [label]="item.status" /></td>
              <td>{{ item.createdAt | date:'dd MMM yyyy':'UTC' }}</td>
              <td><a class="row-action" [routerLink]="['/supervisor/escalations', item.id]" [attr.aria-label]="'Review ' + item.reference">Review &rarr;</a></td></tr>
          }</tbody>
        </table>
      </div>
      @if (!loading() && items().length === 0) {
        <div class="cc-empty" role="status"><h3>No matching escalations</h3><p>There are no escalations matching your search and status filter.</p></div>
      }
      <nav class="panel-footer queue-pagination" aria-label="Escalation queue pagination">
        <span aria-live="polite">Page {{ pageNumber() }}</span>
        <div class="pager">
          <cc-page-size [value]="pageSize()" (valueChange)="setPageSize($event)" />
          <div class="paging">
            <button type="button" aria-label="Previous page" [disabled]="pageNumber() <= 1 || loading()" (click)="previousPage()">&lsaquo;</button>
            <button type="button" aria-label="Next page" [disabled]="!hasNextPage() || loading()" (click)="nextPage()">&rsaquo;</button>
          </div>
        </div>
      </nav>
    </section>`,
})
export class SupervisorQueuePage {
  private readonly api = inject(SupervisorEscalationsApi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly heading = viewChild<ElementRef<HTMLElement>>('listHeading');
  private readonly cursors: (string | null)[] = [null];
  private requestId = 0;
  private applied: { search: string; status: EscalationStatus | '' } = { search: '', status: '' };
  private readonly filterDebounce = createFilterDebouncer(this.destroyRef, () => this.applyFilters());
  protected readonly search = signal('');
  protected readonly status = signal<EscalationStatus | ''>('');
  protected readonly pageSize = signal<PageSize>(defaultPageSize);
  protected readonly pageNumber = signal(1);
  protected readonly page = signal<SupervisorEscalationPage | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal('');
  protected readonly items = computed(() => this.page()?.items ?? []);
  protected readonly hasNextPage = computed(() => this.page()?.pageInfo.hasNextPage === true);
  protected readonly open = computed(() => this.items().filter(item => item.status === 'Open').length);
  protected readonly acknowledged = computed(() => this.items().filter(item => item.status === 'Acknowledged').length);
  protected readonly resolved = computed(() => this.items().filter(item => item.status === 'Resolved').length);

  constructor() {
    this.load();
  }

  /** Text search applies after a typing pause. */
  protected setSearch(value: string): void {
    this.search.set(value);
    this.filterDebounce.schedule();
  }

  protected applyNow(): void {
    this.filterDebounce.now();
  }

  protected setStatus(value: string): void {
    this.status.set(escalationStatuses.find(status => status === value) ?? '');
    this.applyFilters();
  }

  private applyFilters(): void {
    this.filterDebounce.cancel();
    const next = { search: this.search().trim(), status: this.status() };
    if (next.search === this.applied.search && next.status === this.applied.status) return;
    this.applied = next;
    this.resetPaging();
  }

  protected setPageSize(size: PageSize): void {
    this.pageSize.set(size);
    this.resetPaging();
  }

  protected load(): void {
    const requestId = ++this.requestId;
    this.loading.set(true);
    this.error.set('');
    this.api.list(this.applied.search, this.applied.status, this.cursors[this.pageNumber() - 1] ?? null, this.pageSize())
      .pipe(takeUntilDestroyed(this.destroyRef), finalize(() => {
        if (requestId === this.requestId) this.loading.set(false);
      }))
      .subscribe({
        next: result => { if (requestId === this.requestId) this.page.set(result); },
        error: () => {
          if (requestId === this.requestId) this.error.set('Unable to load escalations. Check your Supervisor session and retry.');
        },
      });
  }

  protected previousPage(): void {
    if (this.pageNumber() <= 1) return;
    this.pageNumber.update(page => page - 1);
    this.load();
    this.heading()?.nativeElement.focus();
  }

  protected nextPage(): void {
    const nextCursor = this.page()?.pageInfo.nextCursor;
    if (nextCursor === undefined) return;
    this.cursors[this.pageNumber()] = nextCursor;
    this.pageNumber.update(page => page + 1);
    this.load();
    this.heading()?.nativeElement.focus();
  }

  private resetPaging(): void {
    this.pageNumber.set(1);
    this.cursors.splice(0, this.cursors.length, null);
    this.load();
  }
}
