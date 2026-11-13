import { DatePipe } from '@angular/common';
import { Component, computed, ElementRef, inject, signal, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { DemoWorkspace } from './demo-workspace';
import { DemoHeading, DemoPagination, DemoStat, DemoStatus } from './shared';

@Component({
  selector: 'cc-demo-supervisor-queue',
  imports: [DatePipe, RouterLink, DemoHeading, DemoPagination, DemoStat, DemoStatus],
  template: `
    <cc-demo-heading title="Escalations queue" eyebrow="Supervisor workspace"
      description="Cases waiting for specialist oversight and a recorded outcome.">
      <span class="info-token">SP &middot; Supervisor</span>
    </cc-demo-heading>
    <div class="stats-grid">
      <cc-demo-stat label="Open escalations" [value]="open()" hint="Awaiting acknowledgement" />
      <cc-demo-stat label="Acknowledged" [value]="acknowledged()" hint="Pending resolution" />
      <cc-demo-stat label="Resolved" [value]="resolved()" hint="Internal review complete; no money moved" />
    </div>
    <section class="panel" aria-labelledby="supervisor-queue-heading">
      <div class="panel-title"><h2 id="supervisor-queue-heading" tabindex="-1" #listHeading>Needs your attention</h2></div>
      <div class="filter-bar operations-filters">
        <label class="cc-field search-field"><span>Search escalations</span>
          <input class="cc-control" #query type="search" placeholder="Search by reference or customer"
            (input)="search.set(query.value); page.set(1)"></label>
        <label class="cc-field"><span>Escalation status</span><select class="cc-control" #state (change)="status.set(state.value); page.set(1)">
          <option value="active">Open and acknowledged</option><option value="">All statuses</option>
          <option>Open</option><option>Acknowledged</option><option>Resolved</option>
        </select></label>
      </div>
      <div class="cc-table-scroll" role="region" aria-label="Escalation queue, scroll horizontally on small screens" tabindex="0">
        <table class="cc-table"><caption class="cc-sr-only">Synthetic escalations for supervisor review</caption>
          <thead><tr><th scope="col">Escalation ID</th><th scope="col">Dispute</th><th scope="col">Customer</th>
            <th scope="col">Reason</th><th scope="col">Status</th><th scope="col">Created</th><th scope="col"><span class="cc-sr-only">Action</span></th></tr></thead>
          <tbody>@for (item of visible(); track item.id) {
            <tr><td><a [routerLink]="['/agent/escalations', item.id]">{{ item.reference }}</a></td>
              <td>{{ workspace.agentCase(item.caseId)?.reference }}</td><td>{{ workspace.agentCase(item.caseId)?.customerName }}</td>
              <td>{{ item.reason }}</td><td><cc-demo-status [label]="item.status" /></td>
              <td>{{ item.createdAt | date:'dd MMM yyyy':'UTC' }}</td>
              <td><a class="row-action" [routerLink]="['/agent/escalations', item.id]" [attr.aria-label]="'Review ' + item.reference">Review &rarr;</a></td></tr>
          }</tbody>
        </table>
      </div>
      @if (filtered().length === 0) {
        <div class="cc-empty" role="status"><h3>No matching escalations</h3><p>There are no escalations matching your search and status filter.</p></div>
      }
      <cc-demo-pagination [page]="page()" [total]="filtered().length" (changed)="changePage($event)" />
    </section>`,
})
export class DemoSupervisorQueuePage {
  protected readonly workspace = inject(DemoWorkspace);
  private readonly heading = viewChild<ElementRef<HTMLElement>>('listHeading');
  protected readonly search = signal('');
  protected readonly status = signal('active');
  protected readonly page = signal(1);
  protected readonly open = computed(() => this.workspace.escalations().filter(item => item.status === 'Open').length);
  protected readonly acknowledged = computed(() => this.workspace.escalations().filter(item => item.status === 'Acknowledged').length);
  protected readonly resolved = computed(() => this.workspace.escalations().filter(item => item.status === 'Resolved').length);
  protected readonly filtered = computed(() => this.workspace.escalations().filter(item => {
    const dispute = this.workspace.agentCase(item.caseId);
    return (this.status() === '' || (this.status() === 'active' ? item.status !== 'Resolved' : item.status === this.status())) &&
      `${item.reference} ${dispute?.reference} ${dispute?.customerName}`.toLowerCase().includes(this.search().trim().toLowerCase());
  }));
  protected readonly visible = computed(() => this.filtered().slice((this.page() - 1) * 6, this.page() * 6));
  protected changePage(page: number): void { this.page.set(page); this.heading()?.nativeElement.focus(); }
}
