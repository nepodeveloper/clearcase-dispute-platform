import { DatePipe } from '@angular/common';
import { Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { DemoWorkspace } from './demo-workspace';
import { DemoHeading, DemoMoney, DemoStat, DemoStatus, transactionTypeLabel } from './shared';

@Component({
  selector: 'cc-demo-disputes',
  imports: [DatePipe, RouterLink, DemoHeading, DemoMoney, DemoStat, DemoStatus],
  template: `
    <cc-demo-heading title="My disputes" eyebrow="Customer portal" description="Follow every case from submission to final outcome.">
      <a class="cc-button cc-button--primary" routerLink="/">+ Raise a dispute</a>
    </cc-demo-heading>
    <div class="stats-grid">
      <cc-demo-stat label="All cases" [value]="cases().length" hint="Submitted in this demo" />
      <cc-demo-stat label="In progress" [value]="cases().length - resolved()" hint="Investigation or specialist review" />
      <cc-demo-stat label="Resolved" [value]="resolved()" hint="No real money is moved" />
    </div>
    <section class="panel" aria-labelledby="cases-heading">
      <div class="panel-title"><h2 id="cases-heading">Your dispute cases</h2><span class="muted">Most recent first</span></div>
      <div class="case-grid">
        @for (item of cases(); track item.id) {
          <a class="case-card" [routerLink]="['/disputes', item.id]">
            <div class="case-card-top"><strong>{{ item.reference }}</strong><cc-demo-status [label]="item.caseStatus" /></div>
            <h3>{{ item.merchant }}</h3>
            <p>{{ typeLabel(item.type) }} &middot; {{ item.amountMinor | demoMoney }}</p>
            <div class="case-foot"><span>Updated <time [attr.datetime]="item.updatedAt">{{ item.updatedAt | date:'dd MMM yyyy':'UTC' }}</time></span><strong>View timeline &rarr;</strong></div>
          </a>
        } @empty {
          <div class="cc-empty" role="status"><h3>No disputes submitted</h3><a routerLink="/">Browse your transactions</a></div>
        }
      </div>
    </section>`,
})
export class DemoDisputesPage {
  private readonly workspace = inject(DemoWorkspace);
  protected readonly cases = this.workspace.customerCases;
  protected readonly resolved = computed(() => this.cases().filter(item => item.caseStatus === 'Resolved').length);
  protected readonly typeLabel = transactionTypeLabel;
}
