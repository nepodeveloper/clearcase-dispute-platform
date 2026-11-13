import { DatePipe } from '@angular/common';
import { Component, computed, ElementRef, inject, signal, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { DEMO_DATE } from './demo-data';
import { DemoWorkspace } from './demo-workspace';
import { DemoHeading, DemoMoney, DemoPagination, DemoStat, DemoStatus, transactionTypeLabel } from './shared';

@Component({
  selector: 'cc-demo-transactions',
  imports: [DatePipe, RouterLink, DemoHeading, DemoStat, DemoStatus, DemoMoney, DemoPagination],
  template: `
    <cc-demo-heading title="Your transactions" eyebrow="Customer portal"
      description="Browse recent activity and raise a dispute if something doesn't look right.">
      <span class="info-token">Main account &middot; &bull;&bull;&bull;&bull; 4821</span>
    </cc-demo-heading>
    <div class="stats-grid">
      <cc-demo-stat label="Transactions this month" [value]="workspace.transactions.length" hint="Across 3 transaction types" />
      <cc-demo-stat label="Open disputes" [value]="openCases()" hint="Cases currently under review" />
      <div class="stat-card cta-stat"><small>Need help?</small><strong>We're here to help.</strong>
        <a routerLink="/disputes">Track existing disputes &rarr;</a></div>
    </div>
    <section class="panel" aria-labelledby="transactions-heading">
      <div class="panel-title"><h2 id="transactions-heading" tabindex="-1" #listHeading>Recent transactions</h2>
        <span class="muted">Showing {{ visible().length }} of {{ filtered().length }}</span></div>
      <div class="filter-bar">
        <label class="search-field cc-field"><span>Search transactions</span>
          <input class="cc-control" #searchBox type="search" placeholder="Search by merchant, reference or amount"
            [value]="search()" (input)="search.set(searchBox.value); page.set(1)">
        </label>
        <label class="cc-field"><span>Date range</span>
          <select class="cc-control" #range (change)="days.set(+range.value); page.set(1)">
            <option value="30">Last 30 days</option><option value="7">Last 7 days</option>
          </select></label>
        <label class="cc-field"><span>Transaction type</span>
          <select class="cc-control" #type (change)="category.set(type.value); page.set(1)">
            <option value="">All transaction types</option><option value="CardPurchase">Card purchases</option>
            <option value="DebitOrder">Debit orders</option><option value="BankCharge">Bank charges</option>
          </select></label>
      </div>
      <div class="cc-table-scroll" role="region" aria-label="Recent transactions, scroll horizontally on small screens" tabindex="0">
        <table class="cc-table">
          <caption class="cc-sr-only">Synthetic transactions, newest first</caption>
          <thead><tr><th scope="col">Date</th><th scope="col">Description</th><th scope="col">Category</th>
            <th scope="col">Amount</th><th scope="col">Status</th><th scope="col"><span class="cc-sr-only">Action</span></th></tr></thead>
          <tbody>
            @for (item of visible(); track item.id) {
              <tr>
                <td><time [attr.datetime]="item.occurredAt">{{ item.occurredAt | date:'dd MMM yyyy':'UTC' }}</time></td>
                <td><strong>{{ item.description }}</strong><small>{{ item.type === 'CardPurchase' ? 'Card \u2022\u2022\u2022\u2022 4821' : typeLabel(item.type) }}</small></td>
                <td><span class="tag" [class.tag-cyan]="item.type === 'DebitOrder'" [class.tag-violet]="item.type === 'BankCharge'">{{ typeLabel(item.type) }}</span></td>
                <td class="money">&minus;{{ item.amountMinor | demoMoney }}</td>
                <td><cc-demo-status label="Posted" /></td>
                <td><a class="row-action" [routerLink]="['/transactions', item.id]"
                  [attr.aria-label]="'View and dispute ' + item.description">View &amp; dispute &rarr;</a></td>
              </tr>
            }
          </tbody>
        </table>
      </div>
      @if (filtered().length === 0) {
        <div class="cc-empty" role="status"><h3>No transactions found</h3><p>Try another search or change your filters.</p></div>
      }
      <cc-demo-pagination [page]="page()" [total]="filtered().length" (changed)="changePage($event)" />
      <p class="field-hint">Demo dates are fixed at 13 November 2026. All amounts and account details are synthetic.</p>
    </section>`,
})
export class DemoTransactionsPage {
  protected readonly workspace = inject(DemoWorkspace);
  protected readonly typeLabel = transactionTypeLabel;
  protected readonly search = signal('');
  protected readonly category = signal('');
  protected readonly days = signal(30);
  protected readonly page = signal(1);
  private readonly heading = viewChild<ElementRef<HTMLElement>>('listHeading');
  protected readonly openCases = computed(() => this.workspace.customerCases().filter(item => item.caseStatus !== 'Resolved').length);
  protected readonly filtered = computed(() => {
    const query = this.search().trim().toLowerCase().replace(/^r\s*(?=\d)/, '');
    const since = Date.parse(DEMO_DATE.slice(0, 10)) - (this.days() - 1) * 86400000;
    return this.workspace.transactions.filter(item =>
      (this.category() === '' || item.type === this.category()) &&
      Date.parse(item.occurredAt) >= since &&
      `${item.description} ${item.reference} ${(item.amountMinor / 100).toFixed(2)}`.toLowerCase().includes(query));
  });
  protected readonly visible = computed(() => this.filtered().slice((this.page() - 1) * 6, this.page() * 6));

  protected changePage(page: number): void {
    this.page.set(page);
    this.heading()?.nativeElement.focus();
  }
}
