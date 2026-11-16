import { DatePipe } from '@angular/common';
import { Component, computed, input, Pipe, PipeTransform } from '@angular/core';

export interface TimelineEvent {
  id: string;
  title: string;
  description: string;
  occurredAt: string;
}

@Pipe({ name: 'minorUnitsMoney' })
export class MinorUnitsMoney implements PipeTransform {
  transform(amountMinorUnits: string): string {
    if (!/^-?\d+$/.test(amountMinorUnits)) throw new Error('Expected integer minor units.');
    // BigInt keeps amounts beyond Number.MAX_SAFE_INTEGER exact.
    const minor = BigInt(amountMinorUnits);
    const negative = minor < 0n;
    const absolute = negative ? -minor : minor;
    const whole = (absolute / 100n).toLocaleString('en-GB');
    const fraction = (absolute % 100n).toString().padStart(2, '0');
    return `R ${negative ? '-' : ''}${whole}.${fraction}`;
  }
}

@Component({
  selector: 'cc-page-heading',
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
export class PageHeading {
  readonly title = input.required<string>();
  readonly description = input('');
  readonly eyebrow = input('');
}

@Component({
  selector: 'cc-stat',
  template: `<div class="stat-card"><small>{{ label() }}</small>
    <strong>{{ value() }}</strong><span>{{ hint() }}</span></div>`,
})
export class StatCard {
  readonly label = input.required<string>();
  readonly value = input.required<string | number>();
  readonly hint = input('');
}

const statusTones: Readonly<Record<string, string>> = {
  Posted: 'success', Resolved: 'success', Closed: 'success', ReversalConfirmed: 'success',
  ManualReview: 'review',
  Open: 'warning', Submitted: 'warning',
  Rejected: 'error',
};

@Component({
  selector: 'cc-status-badge',
  template: `<span [class]="'cc-status cc-status--' + tone()">{{ label() }}</span>`,
})
export class StatusBadge {
  readonly label = input.required<string>();
  /** Status code that selects the colour when the displayed label is a friendlier wording. */
  readonly status = input<string>();
  protected readonly tone = computed(() => statusTones[this.status() ?? this.label()] ?? 'progress');
}

@Component({
  selector: 'cc-timeline',
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
      @if (pendingTitle() && !complete()) {
        <li><span class="time-point" aria-hidden="true">&cir;</span>
          <h3>{{ pendingTitle() }}</h3><span class="muted">Pending</span>
          <p>{{ pendingDescription() }}</p>
        </li>
      }
    </ol>`,
})
export class Timeline {
  readonly events = input.required<readonly TimelineEvent[]>();
  readonly complete = input(false);
  readonly label = input('Dispute timeline');
  /** Leave empty to list recorded events only, without a placeholder for the next step. */
  readonly pendingTitle = input('');
  readonly pendingDescription = input('');
}
