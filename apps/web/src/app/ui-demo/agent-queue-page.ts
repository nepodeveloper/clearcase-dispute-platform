import { Component, computed, ElementRef, inject, signal, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { DEMO_DATE } from './demo-data';
import { DemoWorkspace } from './demo-workspace';
import { DemoHeading, DemoMoney, DemoPagination, DemoStat, DemoStatus, transactionTypeLabel } from './shared';

@Component({
  selector: 'cc-demo-agent-queue',
  imports: [RouterLink, DemoHeading, DemoMoney, DemoPagination, DemoStat, DemoStatus],
  template: `
    <cc-demo-heading title="Dispute work queue" eyebrow="Operations workspace"
      description="Review incoming cases, prioritise investigations and refer complex issues.">
      <span class="info-token">AG &middot; Agent</span>
    </cc-demo-heading>
    <div class="stats-grid">
      <cc-demo-stat label="Open cases" [value]="open()" hint="Across all case categories" />
      <cc-demo-stat label="Needs review" [value]="needsReview()" hint="Awaiting investigation" />
      <cc-demo-stat label="Escalated" [value]="escalated()" hint="Under specialist review" />
    </div>
    <section class="panel" aria-labelledby="queue-heading">
      <div class="panel-title"><h2 id="queue-heading" tabindex="-1" #listHeading>Assigned case queue</h2><span class="muted">Operational demo</span></div>
      <div class="filter-bar operations-filters">
        <label class="cc-field search-field"><span>Search cases</span>
          <input class="cc-control" type="search" #query placeholder="Search case reference or customer"
            (input)="search.set(query.value); page.set(1)"></label>
        <label class="cc-field"><span>Case status</span><select class="cc-control" #statusFilter (change)="status.set(statusFilter.value); page.set(1)">
          <option value="">All statuses</option><option>Submitted</option><option>Under review</option><option>Specialist review</option><option>Resolved</option>
        </select></label>
        <label class="cc-field"><span>Dispute type</span><select class="cc-control" #type (change)="category.set(type.value); page.set(1)">
          <option value="">All types</option><option value="CardPurchase">Card purchases</option>
          <option value="DebitOrder">Debit orders</option><option value="BankCharge">Bank charges</option>
        </select></label>
      </div>
      <div class="cc-table-scroll" role="region" aria-label="Case queue, scroll horizontally on small screens" tabindex="0">
        <table class="cc-table"><caption class="cc-sr-only">Synthetic assigned dispute cases</caption>
          <thead><tr><th scope="col">Case reference</th><th scope="col">Customer</th><th scope="col">Category</th>
            <th scope="col">Amount</th><th scope="col">Status</th><th scope="col">Age</th><th scope="col"><span class="cc-sr-only">Action</span></th></tr></thead>
          <tbody>@for (item of visible(); track item.id) {
            <tr><td><a [routerLink]="['/agent/disputes', item.id]"><strong>{{ item.reference }}</strong></a></td>
              <td>{{ item.customerName }}</td>
              <td><span class="tag" [class.tag-cyan]="item.type === 'DebitOrder'" [class.tag-violet]="item.type === 'BankCharge'">{{ typeLabel(item.type) }}</span></td>
              <td class="money">{{ item.amountMinor | demoMoney }}</td><td><cc-demo-status [label]="item.caseStatus" /></td>
              <td>{{ age(item.createdAt) }} days</td>
              <td><a class="row-action" [routerLink]="['/agent/disputes', item.id]" [attr.aria-label]="'Investigate ' + item.reference">Investigate &rarr;</a></td>
            </tr>
          }</tbody>
        </table>
      </div>
      @if (filtered().length === 0) {
        <div class="cc-empty" role="status"><h3>No matching cases</h3><p>Try another reference, customer or filter.</p></div>
      }
      <cc-demo-pagination [page]="page()" [total]="filtered().length" (changed)="changePage($event)" />
    </section>`,
})
export class DemoAgentQueuePage {
  private readonly workspace = inject(DemoWorkspace);
  private readonly heading = viewChild<ElementRef<HTMLElement>>('listHeading');
  protected readonly typeLabel = transactionTypeLabel;
  protected readonly search = signal('');
  protected readonly status = signal('');
  protected readonly category = signal('');
  protected readonly page = signal(1);
  protected readonly open = computed(() => this.workspace.agentCases().filter(item => item.caseStatus !== 'Resolved').length);
  protected readonly needsReview = computed(() => this.workspace.agentCases().filter(item => item.caseStatus === 'Submitted' || item.caseStatus === 'Under review').length);
  protected readonly escalated = computed(() => this.workspace.agentCases().filter(item => item.caseStatus === 'Specialist review').length);
  protected readonly filtered = computed(() => this.workspace.agentCases().filter(item =>
    (this.status() === '' || item.caseStatus === this.status()) &&
    (this.category() === '' || item.type === this.category()) &&
    `${item.reference} ${item.customerName} ${item.merchant}`.toLowerCase().includes(this.search().trim().toLowerCase())));
  protected readonly visible = computed(() => this.filtered().slice((this.page() - 1) * 6, this.page() * 6));
  protected age(date: string): number { return Math.max(0, Math.floor((Date.parse(DEMO_DATE) - Date.parse(date)) / 86400000)); }
  protected changePage(page: number): void { this.page.set(page); this.heading()?.nativeElement.focus(); }
}
