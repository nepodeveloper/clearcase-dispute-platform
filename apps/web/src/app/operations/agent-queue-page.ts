import { Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { transactionTypeLabel } from '../transactions/transaction-type-label';
import { defaultPageSize, MinorUnitsMoney, PageHeading, PageSize, PageSizeSelect, StatCard, StatusBadge } from '../ui/ui-components';
import {
  AgentCaseStatus,
  agentCaseStatusLabel,
  AgentDisputeCategory,
  AgentDisputeFilters,
  AgentDisputePage,
  AgentDisputesApi,
} from './agent-disputes-api';

const caseStatuses: readonly AgentCaseStatus[] = ['Submitted', 'ManualReview', 'UnderReview', 'MandateVerified',
  'ReversalRequested', 'ReversalConfirmed', 'Closed', 'Rejected'];
const categories: readonly AgentDisputeCategory[] = ['CardPurchase', 'DebitOrder', 'BankCharge'];
const finishedStatuses: readonly AgentCaseStatus[] = ['Closed', 'Rejected', 'ReversalConfirmed'];
/** Cases open for more than this many days are flagged as aging in the queue. */
export const agingThresholdDays = 5;

@Component({
  selector: 'cc-agent-queue',
  imports: [RouterLink, MinorUnitsMoney, PageHeading, PageSizeSelect, StatCard, StatusBadge],
  template: `
    <cc-page-heading title="Dispute work queue" eyebrow="Operations workspace"
      description="Review incoming cases, prioritise investigations and refer complex issues.">
      <span class="info-token">AG &middot; Agent</span>
    </cc-page-heading>
    <div class="stats-grid">
      <cc-stat label="Open cases" [value]="open()" hint="In current results" />
      <cc-stat label="Needs review" [value]="needsReview()" hint="In current results" />
      <cc-stat label="Escalated" [value]="escalated()" hint="In current results" />
    </div>
    <section class="panel" aria-labelledby="queue-heading">
      <div class="panel-title"><h2 id="queue-heading">Assigned case queue</h2>
        <span class="muted">Persisted operational cases</span></div>
      <div class="filter-bar operations-filters">
        <label class="cc-field search-field"><span>Search cases</span>
          <input class="cc-control" type="search" #query placeholder="Search case reference or customer"
            [value]="search()" (input)="search.set(query.value)"></label>
        <label class="cc-field"><span>Case status</span>
          <select class="cc-control" #statusFilter (change)="setStatus(statusFilter.value)">
            <option value="">All statuses</option>
            @for (status of statuses; track status) { <option [value]="status">{{ statusLabel(status) }}</option> }
          </select></label>
        <label class="cc-field"><span>Dispute type</span>
          <select class="cc-control" #type (change)="setCategory(type.value)">
            <option value="">All types</option>
            @for (category of categories; track category) { <option [value]="category">{{ typeLabel(category) }}</option> }
          </select></label>
        <button class="cc-button cc-button--secondary" type="button" [disabled]="loading()"
          (click)="applyFilters()">Search</button>
      </div>
      @if (error()) {
        <div class="cc-form-error" role="alert"><p>{{ error() }}</p>
          <button class="cc-button cc-button--secondary" type="button" (click)="load()">Retry</button>
        </div>
      }
      @if (loading()) { <p class="muted" role="status">Loading persisted cases...</p> }
      <div class="cc-table-scroll" role="region" aria-label="Case queue, scroll horizontally on small screens" tabindex="0">
        <table class="cc-table"><caption class="cc-sr-only">Dispute cases in the authorized Agent scope</caption>
          <thead><tr><th scope="col">Case reference</th><th scope="col">Customer</th><th scope="col">Category</th>
            <th scope="col">Amount</th><th scope="col">Status</th><th scope="col">Age</th><th scope="col"><span class="cc-sr-only">Action</span></th></tr></thead>
          <tbody>@for (item of items(); track item.id) {
            <tr><td><a [routerLink]="['/agent/disputes', item.id]"><strong>{{ item.reference }}</strong></a></td>
              <td>{{ item.customerReference }}</td>
              <td><span class="tag" [class.tag-cyan]="item.category === 'DebitOrder'" [class.tag-violet]="item.category === 'BankCharge'">{{ typeLabel(item.category) }}</span></td>
              <td class="money">{{ item.amountMinorUnits | minorUnitsMoney }}</td>
              <td><cc-status-badge [label]="statusLabel(item.caseStatus)" [status]="item.caseStatus" /></td>
              <td>{{ ageLabel(item.createdAt) }}
                @if (isAging(item.createdAt)) {
                  <span class="tag tag-warning">Aging<span class="cc-sr-only"> case, open for more than {{ agingThresholdDays }} days</span></span>
                }</td>
              <td><a class="row-action" [routerLink]="['/agent/disputes', item.id]" [attr.aria-label]="'Investigate ' + item.reference">Investigate &rarr;</a></td>
            </tr>
          }</tbody>
        </table>
      </div>
      @if (!loading() && items().length === 0) {
        <div class="cc-empty" role="status"><h3>No matching cases</h3><p>Try another reference, customer or filter.</p></div>
      }
      <nav class="panel-footer" aria-label="Case queue pagination">
        <span>Page {{ pageNumber() }}</span>
        <div class="pager">
          <cc-page-size [value]="pageSize()" (valueChange)="setPageSize($event)" />
          <div class="paging">
            <button type="button" aria-label="Previous page" [disabled]="pageNumber() <= 1 || loading()" (click)="previousPage()">&lsaquo;</button>
            <button type="button" aria-label="Next page" data-testid="next" [disabled]="!hasNextPage() || loading()" (click)="nextPage()">&rsaquo;</button>
          </div>
        </div>
      </nav>
    </section>`,
})
export class AgentQueuePage {
  private readonly api = inject(AgentDisputesApi);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly typeLabel = transactionTypeLabel;
  protected readonly statusLabel = agentCaseStatusLabel;
  protected readonly statuses = caseStatuses;
  protected readonly categories = categories;
  protected readonly agingThresholdDays = agingThresholdDays;
  protected readonly search = signal('');
  protected readonly status = signal<AgentCaseStatus | ''>('');
  protected readonly category = signal<AgentDisputeCategory | ''>('');
  protected readonly pageNumber = signal(1);
  protected readonly pageSize = signal<PageSize>(defaultPageSize);
  protected readonly loading = signal(false);
  protected readonly error = signal('');
  protected readonly page = signal<AgentDisputePage | null>(null);
  private cursors: (string | null)[] = [null];
  private requestId = 0;
  private activeFilters: AgentDisputeFilters = { search: '', category: '', caseStatus: '' };
  protected readonly items = computed(() => this.page()?.items ?? []);
  protected readonly hasNextPage = computed(() => this.page()?.pageInfo.hasNextPage === true);
  protected readonly open = computed(() => this.items().filter(item => !finishedStatuses.includes(item.caseStatus)).length);
  protected readonly needsReview = computed(() =>
    this.items().filter(item => item.caseStatus === 'Submitted' || item.caseStatus === 'UnderReview').length);
  protected readonly escalated = computed(() => this.items().filter(item => item.caseStatus === 'ManualReview').length);

  constructor() {
    this.load();
  }

  protected load(): void {
    const requestId = ++this.requestId;
    this.loading.set(true);
    this.error.set('');
    this.api.list(this.activeFilters, this.cursors[this.pageNumber() - 1] ?? null, this.pageSize())
      .pipe(takeUntilDestroyed(this.destroyRef), finalize(() => {
        if (requestId === this.requestId) this.loading.set(false);
      }))
      .subscribe({
        next: value => { if (requestId === this.requestId) this.page.set(value); },
        error: () => {
          if (requestId === this.requestId) this.error.set('Unable to load persisted cases. Check your Agent session and retry.');
        },
      });
  }

  protected applyFilters(): void {
    this.activeFilters = { search: this.search(), category: this.category(), caseStatus: this.status() };
    this.resetPaging();
  }

  protected setPageSize(size: PageSize): void {
    this.pageSize.set(size);
    this.resetPaging();
  }

  private resetPaging(): void {
    this.cursors = [null];
    this.pageNumber.set(1);
    this.load();
  }

  protected nextPage(): void {
    const cursor = this.page()?.pageInfo.nextCursor;
    if (cursor === undefined || this.loading()) return;
    this.cursors = this.cursors.slice(0, this.pageNumber());
    this.cursors.push(cursor);
    this.pageNumber.update(value => value + 1);
    this.load();
  }

  protected previousPage(): void {
    if (this.pageNumber() <= 1 || this.loading()) return;
    this.pageNumber.update(value => value - 1);
    this.load();
  }

  protected setStatus(value: string): void {
    this.status.set(caseStatuses.find(item => item === value) ?? '');
  }

  protected setCategory(value: string): void {
    this.category.set(categories.find(item => item === value) ?? '');
  }

  protected age(date: string): number {
    return Math.max(0, Math.floor((Date.now() - Date.parse(date)) / 86400000));
  }

  protected ageLabel(date: string): string {
    const days = this.age(date);
    return days === 0 ? 'Opened today' : `Open for ${days} ${days === 1 ? 'day' : 'days'}`;
  }

  protected isAging(date: string): boolean {
    return this.age(date) > agingThresholdDays;
  }
}
