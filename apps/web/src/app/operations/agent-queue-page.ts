import { Component, computed, DestroyRef, ElementRef, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import {
  AgentCaseStatus,
  AgentDisputeCategory,
  AgentDisputeFilters,
  AgentDisputeListItem,
  AgentDisputePage,
} from '../agent/agent-disputes-api';
import { DEMO_DATE } from './demo-data';
import { DemoWorkspace } from './demo-workspace';
import { OPERATIONS_API } from './operations-api';
import { DemoHeading, DemoMoney, DemoPagination, DemoStat, DemoStatus, transactionTypeLabel } from './shared';

interface QueueCase {
  id: string;
  reference: string;
  customer: string;
  category: AgentDisputeCategory;
  amountMinor: string;
  caseStatus: string;
  createdAt: string;
  description: string;
}

@Component({
  selector: 'cc-demo-agent-queue',
  host: { class: 'operations-content' },
  imports: [RouterLink, DemoHeading, DemoMoney, DemoPagination, DemoStat, DemoStatus],
  template: `
    <cc-demo-heading title="Dispute work queue" eyebrow="Operations workspace"
      description="Review incoming cases, prioritise investigations and refer complex issues.">
      <span class="info-token">AG &middot; Agent</span>
    </cc-demo-heading>
    <div class="stats-grid">
      <cc-demo-stat label="Open cases" [value]="open()" hint="In current results" />
      <cc-demo-stat label="Needs review" [value]="needsReview()" hint="In current results" />
      <cc-demo-stat label="Escalated" [value]="escalated()" hint="In current results" />
    </div>
    <section class="panel" aria-labelledby="queue-heading">
      <div class="panel-title"><h2 id="queue-heading" tabindex="-1" #listHeading>Assigned case queue</h2>
        <span class="muted">{{ api === null ? 'Operational demo' : 'Persisted operational cases' }}</span></div>
      <div class="filter-bar operations-filters">
        <label class="cc-field search-field"><span>Search cases</span>
          <input class="cc-control" type="search" #query placeholder="Search case reference or customer"
              [value]="search()" (input)="search.set(query.value)"></label>
          <label class="cc-field"><span>Case status</span><select class="cc-control" #statusFilter (change)="setStatus(statusFilter.value)">
            <option value="">All statuses</option><option value="Submitted">Submitted</option>
            <option value="ManualReview">Manual review</option><option value="UnderReview">Under review</option>
            <option value="MandateVerified">Mandate verified</option><option value="ReversalRequested">Reversal requested</option>
            <option value="ReversalConfirmed">Reversal confirmed</option><option value="Closed">Closed</option><option value="Rejected">Rejected</option>
        </select></label>
          <label class="cc-field"><span>Dispute type</span><select class="cc-control" #type (change)="setCategory(type.value)">
          <option value="">All types</option><option value="CardPurchase">Card purchases</option>
          <option value="DebitOrder">Debit orders</option><option value="BankCharge">Bank charges</option>
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
          <tbody>@for (item of visible(); track item.id) {
            <tr><td><a [routerLink]="['/agent/disputes', item.id]"><strong>{{ item.reference }}</strong></a></td>
              <td>{{ item.customer }}</td>
              <td><span class="tag" [class.tag-cyan]="item.category === 'DebitOrder'" [class.tag-violet]="item.category === 'BankCharge'">{{ typeLabel(item.category) }}</span></td>
              <td class="money">{{ item.amountMinor | demoMoney }}</td><td><cc-demo-status [label]="item.caseStatus" /></td>
              <td>{{ age(item.createdAt) }} days</td>
              <td><a class="row-action" [routerLink]="['/agent/disputes', item.id]" [attr.aria-label]="'Investigate ' + item.reference">Investigate &rarr;</a></td>
            </tr>
          }</tbody>
        </table>
      </div>
      @if (!loading() && filtered().length === 0) {
        <div class="cc-empty" role="status"><h3>No matching cases</h3><p>Try another reference, customer or filter.</p></div>
      }
      @if (api === null) {
        <cc-demo-pagination [page]="page()" [total]="filtered().length" (changed)="changePage($event)" />
      } @else {
        <nav class="panel-footer" aria-label="Case queue pagination">
          <span>Page {{ page() }}</span>
          <div class="paging">
            <button type="button" aria-label="Previous page" [disabled]="page() <= 1 || loading()" (click)="previousApiPage()">&lsaquo;</button>
            <button type="button" aria-label="Next page" data-testid="next" [disabled]="!apiPage()?.pageInfo?.hasNextPage || loading()" (click)="nextApiPage()">&rsaquo;</button>
          </div>
        </nav>
      }
    </section>`,
})
export class DemoAgentQueuePage {
  protected readonly api = inject(OPERATIONS_API, { optional: true });
  private readonly workspace = inject(DemoWorkspace, { optional: true });
  private readonly destroyRef = inject(DestroyRef);
  private readonly heading = viewChild<ElementRef<HTMLElement>>('listHeading');
  protected readonly typeLabel = transactionTypeLabel;
  protected readonly search = signal('');
  protected readonly status = signal<AgentCaseStatus | ''>('');
  protected readonly category = signal<AgentDisputeCategory | ''>('');
  protected readonly page = signal(1);
  protected readonly loading = signal(false);
  protected readonly error = signal('');
  protected readonly apiPage = signal<AgentDisputePage | null>(null);
  private cursors: (string | null)[] = [null];
  private requestId = 0;
  private activeFilters: AgentDisputeFilters = { search: '', category: '', caseStatus: '' };
  protected readonly allCases = computed<QueueCase[]>(() => {
    if (this.api !== null)
      return (this.apiPage()?.items ?? []).map(item => this.fromApi(item));
    return (this.workspace?.agentCases() ?? []).map(item => ({
      id: item.id,
      reference: item.reference,
      customer: item.customerName,
      category: item.type,
      amountMinor: item.amountMinor.toString(),
      caseStatus: item.caseStatus,
      createdAt: item.createdAt,
      description: item.merchant,
    }));
  });
  protected readonly open = computed(() => this.allCases().filter(item => !['Resolved', 'Closed', 'Rejected'].includes(item.caseStatus)).length);
  protected readonly needsReview = computed(() => this.allCases().filter(item => ['Submitted', 'Under review', 'UnderReview', 'Manual review', 'ManualReview'].includes(item.caseStatus)).length);
  protected readonly escalated = computed(() => this.allCases().filter(item => item.caseStatus === 'Specialist review').length);
  protected readonly filtered = computed(() => this.api !== null ? this.allCases() : this.allCases().filter(item =>
    (this.status() === '' || item.caseStatus === this.status()) &&
    (this.category() === '' || item.category === this.category()) &&
    `${item.reference} ${item.customer} ${item.description}`.toLowerCase().includes(this.search().trim().toLowerCase())));
  protected readonly visible = computed(() => this.api !== null
    ? this.filtered()
    : this.filtered().slice((this.page() - 1) * 6, this.page() * 6));

  constructor() {
    if (this.api !== null) void this.load();
  }

  protected load(): void {
    if (this.api === null) return;
    const requestId = ++this.requestId;
    this.loading.set(true);
    this.error.set('');
    this.api.agent.list(this.activeFilters, this.cursors[this.page() - 1] ?? null)
      .pipe(takeUntilDestroyed(this.destroyRef), finalize(() => {
        if (requestId === this.requestId) this.loading.set(false);
      }))
      .subscribe({
        next: value => { if (requestId === this.requestId) this.apiPage.set(value); },
        error: () => {
          if (requestId === this.requestId) this.error.set('Unable to load persisted cases. Check your Agent session and retry.');
        },
      });
  }

  protected applyFilters(): void {
    if (this.api === null) { this.page.set(1); return; }
    this.activeFilters = {
      search: this.search(),
      category: this.category(),
      caseStatus: this.status(),
    };
    this.cursors = [null];
    this.page.set(1);
    this.load();
  }

  protected nextApiPage(): void {
    const cursor = this.apiPage()?.pageInfo.nextCursor;
    if (cursor === undefined || this.loading()) return;
    this.cursors = this.cursors.slice(0, this.page());
    this.cursors.push(cursor);
    this.page.update(value => value + 1);
    this.load();
  }

  protected previousApiPage(): void {
    if (this.page() <= 1 || this.loading()) return;
    this.page.update(value => value - 1);
    this.load();
  }

  protected setStatus(value: string): void {
    const statuses: readonly AgentCaseStatus[] = ['Submitted', 'ManualReview', 'Closed', 'UnderReview',
      'MandateVerified', 'ReversalRequested', 'ReversalConfirmed', 'Rejected'];
    this.status.set(statuses.find(item => item === value) ?? '');
  }

  protected setCategory(value: string): void {
    const categories: readonly AgentDisputeCategory[] = ['CardPurchase', 'DebitOrder', 'BankCharge'];
    this.category.set(categories.find(item => item === value) ?? '');
  }

  protected age(date: string): number {
    const now = this.api === null ? Date.parse(DEMO_DATE) : Date.now();
    return Math.max(0, Math.floor((now - Date.parse(date)) / 86400000));
  }

  protected changePage(page: number): void { this.page.set(page); this.heading()?.nativeElement.focus(); }

  private fromApi(item: AgentDisputeListItem): QueueCase {
    return {
      id: item.id,
      reference: item.reference,
      customer: item.customerReference,
      category: item.category,
      amountMinor: item.amountMinorUnits,
      caseStatus: item.caseStatus === 'ManualReview' ? 'Specialist review'
        : item.caseStatus === 'UnderReview' ? 'Under review' : item.caseStatus,
      createdAt: item.createdAt,
      description: item.transactionDescription,
    };
  }
}
