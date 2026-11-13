import { DatePipe } from '@angular/common';
import { Component, computed, input, output, Pipe, PipeTransform } from '@angular/core';
import { DemoEvent } from './demo-data';
export { transactionTypeLabel } from '../transactions/transaction-type-label';

@Pipe({ name: 'demoMoney' })
export class DemoMoney implements PipeTransform {
  transform(amountMinor: number): string {
    return `R ${(amountMinor / 100).toLocaleString('en-GB', {
      minimumFractionDigits: 2, maximumFractionDigits: 2,
    })}`;
  }
}

@Component({
  selector: 'cc-demo-heading',
  template: `
    <div class="screen-head">
      <div>
        <ng-content select="[back]" />
        @if (eyebrow()) { <span class="eyebrow">{{ eyebrow() }}</span> }
        <h1 tabindex="-1">{{ title() }}</h1>
        <p>{{ description() }}</p>
      </div>
      <ng-content />
    </div>`,
})
export class DemoHeading {
  readonly title = input.required<string>();
  readonly description = input('');
  readonly eyebrow = input('');
}

@Component({
  selector: 'cc-demo-stat',
  template: `<div class="stat-card"><small>{{ label() }}</small>
    <strong>{{ value() }}</strong><span>{{ hint() }}</span></div>`,
})
export class DemoStat {
  readonly label = input.required<string>();
  readonly value = input.required<string | number>();
  readonly hint = input('');
}

@Component({
  selector: 'cc-demo-status',
  template: `<span [class]="'cc-status cc-status--' + tone()">{{ label() }}</span>`,
})
export class DemoStatus {
  readonly label = input.required<string>();
  protected readonly tone = computed(() => {
    switch (this.label()) {
      case 'Resolved': case 'Posted': return 'success';
      case 'Specialist review': return 'review';
      case 'Open': case 'Submitted': return 'warning';
      case 'Declined': return 'error';
      default: return 'progress';
    }
  });
}

@Component({
  selector: 'cc-demo-timeline',
  imports: [DatePipe],
  template: `
    <ol class="timeline" [attr.aria-label]="label()">
      @for (event of events(); track event.id; let last = $last) {
        <li [class.complete]="!last || complete()" [class.active]="last && !complete()">
          <span class="time-point" aria-hidden="true">{{ !last || complete() ? '\u2713' : '\u2022' }}</span>
          <h3>{{ event.title }}</h3>
          <time [attr.datetime]="event.occurredAt">{{ event.occurredAt | date:'dd MMM yyyy, HH:mm':'UTC' }} UTC</time>
          <p>{{ event.description }}</p>
        </li>
      }
      @if (!complete()) {
        <li><span class="time-point" aria-hidden="true">&cir;</span>
          <h3>{{ pendingTitle() }}</h3><span class="muted">Pending</span>
          <p>{{ pendingDescription() }}</p>
        </li>
      }
    </ol>`,
})
export class DemoTimeline {
  readonly events = input.required<readonly DemoEvent[]>();
  readonly complete = input(false);
  readonly label = input('Dispute timeline');
  readonly pendingTitle = input('Final outcome');
  readonly pendingDescription = input('We will update the case when a decision and financial outcome are available.');
}

@Component({
  selector: 'cc-demo-pagination',
  template: `
    <nav class="panel-footer" aria-label="Pagination">
      <span aria-live="polite">Showing {{ total() === 0 ? 0 : (page() - 1) * size() + 1 }}&ndash;{{ end() }} of {{ total() }}</span>
      <div class="paging">
        <button type="button" aria-label="Previous page" [disabled]="page() <= 1" (click)="changed.emit(page() - 1)">&lsaquo;</button>
        @for (number of pages(); track number) {
          <button type="button" [attr.aria-label]="'Page ' + number"
            [attr.aria-current]="number === page() ? 'page' : null"
            (click)="changed.emit(number)">{{ number }}</button>
        }
        <button type="button" aria-label="Next page" data-testid="next" [disabled]="page() >= pages().length"
          (click)="changed.emit(page() + 1)">&rsaquo;</button>
      </div>
    </nav>`,
})
export class DemoPagination {
  readonly page = input.required<number>();
  readonly total = input.required<number>();
  readonly size = input(6);
  readonly changed = output<number>();
  protected readonly end = computed(() => Math.min(this.page() * this.size(), this.total()));
  protected readonly pages = computed(() => Array.from(
    { length: Math.ceil(this.total() / this.size()) }, (_, index) => index + 1));
}
