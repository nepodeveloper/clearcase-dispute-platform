import { Component, computed, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { DemoWorkspace } from './demo-workspace';
import { DemoHeading, DemoMoney, DemoStatus, DemoTimeline, transactionTypeLabel } from './shared';

@Component({
  selector: 'cc-demo-case-timeline',
  imports: [RouterLink, DemoHeading, DemoMoney, DemoStatus, DemoTimeline],
  template: `
    @if (dispute(); as item) {
      <cc-demo-heading [title]="'Dispute ' + item.reference"
        [description]="item.merchant + ' \u00b7 ' + typeLabel(item.type) + ' \u00b7 ' + (item.amountMinor | demoMoney)">
        <a back class="back" routerLink="/disputes" data-testid="back-to-disputes">&larr; Back to my disputes</a>
        <cc-demo-status [label]="item.caseStatus" />
      </cc-demo-heading>
      <div class="split-grid unequal">
        <section class="panel" aria-labelledby="timeline-heading">
          <div class="panel-title"><div><h2 id="timeline-heading">Case timeline</h2><p>See how your case is progressing.</p></div></div>
          <cc-demo-timeline [events]="item.timeline" [complete]="item.caseStatus === 'Resolved'" />
        </section>
        <div class="stack">
          <section class="panel"><h2>Case summary</h2>
            <dl class="description-grid"><dt>Case reference</dt><dd>{{ item.reference }}</dd>
              <dt>Case status</dt><dd>{{ item.caseStatus }}</dd>
              <dt>Decision</dt><dd>{{ item.decisionStatus }}</dd>
              <dt>Financial status</dt><dd>{{ item.financialStatus }}</dd>
            </dl>
          </section>
          <aside class="panel info-panel"><h3>Good to know</h3>
            <p>An investigation decision and a confirmed financial reversal are separate events.
              No money moves in this UI demo. Completing specialist review does not close a dispute or confirm a refund.</p>
          </aside>
        </div>
      </div>
    } @else {
      <div class="cc-empty"><h1 tabindex="-1">Dispute not found</h1><p>This case is not available in your customer demo.</p><a routerLink="/disputes">Back to my disputes</a></div>
    }`,
})
export class DemoTimelinePage {
  readonly id = input.required<string>();
  private readonly workspace = inject(DemoWorkspace);
  protected readonly dispute = computed(() => this.workspace.customerCase(this.id()));
  protected readonly typeLabel = transactionTypeLabel;
}
