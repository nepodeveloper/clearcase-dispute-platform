import { DatePipe } from '@angular/common';
import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { DemoWorkspace } from './demo-workspace';
import { DemoHeading, DemoStatus, DemoTimeline } from './shared';

@Component({
  selector: 'cc-demo-supervisor-detail',
  imports: [DatePipe, ReactiveFormsModule, RouterLink, DemoHeading, DemoStatus, DemoTimeline],
  template: `
    @if (escalation(); as item) {
      <cc-demo-heading [title]="'Escalation ' + item.reference" [description]="'Linked dispute ' + dispute()?.reference + ' \u00b7 ' + item.reason">
        <a back class="back" routerLink="/agent/escalations">&larr; Back to escalations</a>
        <cc-demo-status [label]="item.status" />
      </cc-demo-heading>
      @if (confirmation()) { <p class="notice" role="status">{{ confirmation() }}</p> }
      <div class="split-grid">
        <div class="stack">
          <section class="panel"><h2>Escalation overview</h2>
            <dl class="description-grid"><dt>Raised by</dt><dd>Agent &middot; {{ item.createdAt | date:'dd MMM yyyy':'UTC' }}</dd>
              <dt>Linked case</dt><dd>{{ dispute()?.reference }}</dd>
              <dt>Status</dt><dd>{{ item.status }}</dd><dt>Customer</dt><dd>{{ dispute()?.customerName }}</dd>
            </dl>
            <div class="soft-box"><small>Internal note</small><p>{{ item.note }}</p></div>
          </section>
          <section class="panel"><div class="panel-title"><h2>Internal history</h2></div>
            <cc-demo-timeline [events]="item.history" [complete]="item.status === 'Resolved'" label="Internal escalation history"
              [pendingTitle]="item.status === 'Open' ? 'Supervisor acknowledgement' : 'Resolution'"
              pendingDescription="Awaiting the next internal review action." />
          </section>
        </div>
        <div class="stack">
          <section class="panel"><h2>Supervisor actions</h2>
            <p class="muted">Choose an operation only when your role and case state permit it.</p>
            @if (item.status !== 'Resolved') {
              <button class="cc-button cc-button--secondary full" type="button" [disabled]="item.status !== 'Open'" (click)="acknowledge()">
                {{ item.status === 'Open' ? 'Acknowledge demo escalation' : 'Acknowledged' }}
              </button>
              <form class="form-stack note-form" (submit)="resolve($event)" novalidate>
                <div class="cc-field"><label for="resolution-note">Resolution note</label>
                  <textarea class="cc-control" id="resolution-note" rows="4" [formControl]="note" maxlength="2000"
                    placeholder="Record the internal outcome..." aria-describedby="resolution-hint resolution-error"
                    [attr.aria-invalid]="error() !== ''"></textarea>
                  <span class="field-hint" id="resolution-hint">15&ndash;2000 characters. Internal only.
                    @if (item.status === 'Open') { Acknowledge the escalation before resolving. }</span>
                </div>
                <button class="cc-button cc-button--primary full" type="submit" [disabled]="item.status !== 'Acknowledged'">
                  Resolve demo escalation
                </button>
              </form>
            } @else {
              <p class="notice">Specialist review is complete. The linked dispute remains under review.
                This action did not approve a refund or move money.</p>
            }
            <div id="resolution-error">@if (error()) { <p class="cc-form-error" role="alert">{{ error() }}</p> }</div>
          </section>
          <aside class="panel info-panel"><h3>Privacy safeguard</h3>
            <p>Customer timeline shows only a safe progress update. Internal notes are visible only in the operations demo.
              These are synthetic fixtures, not real customer records or authentication.</p>
          </aside>
        </div>
      </div>
    } @else {
      <div class="cc-empty"><h1 tabindex="-1">Escalation not found</h1><a routerLink="/agent/escalations">Back to escalations</a></div>
    }`,
})
export class DemoSupervisorDetailPage {
  readonly id = input.required<string>();
  private readonly workspace = inject(DemoWorkspace);
  protected readonly escalation = computed(() => this.workspace.escalation(this.id()));
  protected readonly dispute = computed(() => {
    const escalation = this.escalation();
    return escalation === undefined ? undefined : this.workspace.agentCase(escalation.caseId);
  });
  protected readonly note = new FormControl('', { nonNullable: true });
  protected readonly error = signal('');
  protected readonly confirmation = signal('');

  constructor() {
    effect(() => {
      this.id();
      this.note.reset();
      this.error.set('');
      this.confirmation.set('');
    });
  }

  protected acknowledge(): void {
    const result = this.workspace.acknowledge(this.id());
    if (!result.ok) { this.error.set(result.message); return; }
    this.error.set('');
    this.confirmation.set('Escalation acknowledged in this demo. Record an internal resolution when your review is complete.');
  }

  protected resolve(event: Event): void {
    event.preventDefault();
    const result = this.workspace.resolve(this.id(), this.note.value);
    if (!result.ok) { this.error.set(result.message); return; }
    this.note.reset();
    this.error.set('');
    this.confirmation.set('Demo escalation resolved. The customer sees a safe progress update; no funds are confirmed.');
  }
}
