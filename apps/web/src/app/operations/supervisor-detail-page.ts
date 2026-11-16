import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { PageHeading, StatusBadge, Timeline, TimelineEvent } from '../ui/ui-components';
import { SupervisorEscalation, SupervisorEscalationsApi } from './supervisor-escalations-api';

@Component({
  selector: 'cc-supervisor-detail',
  imports: [DatePipe, ReactiveFormsModule, RouterLink, PageHeading, StatusBadge, Timeline],
  template: `
    @if (loading() && escalation() === null) {
      <p class="notice" role="status">Loading persisted escalation...</p>
    } @else if (loadError()) {
      <div class="cc-empty" role="alert"><h1 tabindex="-1">Escalation unavailable</h1><p>{{ loadError() }}</p>
        <button class="cc-button cc-button--secondary" type="button" (click)="loadEscalation()">Retry</button>
      </div>
    } @else if (escalation(); as item) {
      <cc-page-heading [title]="'Escalation ' + item.reference"
        [description]="'Linked dispute ' + item.disputeReference + ' \u00b7 ' + item.reason">
        <a back class="back" routerLink="/agent/escalations">&larr; Back to escalations</a>
        <cc-status-badge [label]="item.status" />
      </cc-page-heading>
      @if (confirmation()) { <p class="notice" role="status">{{ confirmation() }}</p> }
      <div class="split-grid">
        <div class="stack">
          <section class="panel"><h2>Escalation overview</h2>
            <dl class="description-grid"><dt>Raised by</dt><dd>Agent &middot; {{ item.createdAt | date:'dd MMM yyyy':'UTC' }}</dd>
              <dt>Linked case</dt><dd>{{ item.disputeReference }}</dd>
              <dt>Status</dt><dd>{{ item.status }}</dd><dt>Customer</dt><dd>{{ item.customerReference }}</dd>
              <dt>Case status</dt><dd>{{ item.caseStatus }}</dd>
              <dt>Decision status</dt><dd>{{ item.decisionStatus }}</dd>
              <dt>Financial status</dt><dd>{{ item.financialStatus }}</dd>
            </dl>
            <div class="soft-box"><small>Internal note</small><p>{{ item.note || 'No private note recorded.' }}</p></div>
          </section>
          <section class="panel"><div class="panel-title"><h2>Internal history</h2></div>
            <cc-timeline [events]="history()" [complete]="item.status === 'Resolved'" label="Internal escalation history"
              [pendingTitle]="item.status === 'Open' ? 'Supervisor acknowledgement' : 'Resolution'"
              pendingDescription="Awaiting the next internal review action." />
          </section>
        </div>
        <div class="stack">
          <section class="panel"><h2>Supervisor actions</h2>
            <p class="muted">Choose an operation only when your role and case state permit it.</p>
            @if (item.status !== 'Resolved') {
              <button class="cc-button cc-button--secondary full" type="button" [disabled]="item.status !== 'Open' || pending()" (click)="acknowledge()">
                {{ item.status === 'Open' ? 'Acknowledge escalation' : 'Acknowledged' }}
              </button>
              <form class="form-stack note-form" (submit)="resolve($event)" novalidate>
                <div class="cc-field"><label for="resolution-note">Resolution note</label>
                  <textarea class="cc-control" id="resolution-note" rows="4" [formControl]="note" maxlength="2000"
                    [disabled]="pending()"
                    placeholder="Record the internal outcome..." aria-describedby="resolution-hint resolution-error"
                    [attr.aria-invalid]="error() !== ''"></textarea>
                  <span class="field-hint" id="resolution-hint">15&ndash;2000 characters. Internal only.
                    @if (item.status === 'Open') { Acknowledge the escalation before resolving. }</span>
                </div>
                <button class="cc-button cc-button--primary full" type="submit" [disabled]="item.status !== 'Acknowledged' || pending()">
                  Resolve escalation
                </button>
              </form>
            } @else {
              <p class="notice">Specialist review is complete. The linked dispute remains under review.
                This action did not approve a refund or move money.</p>
            }
            <div id="resolution-error">@if (error()) { <p class="cc-form-error" role="alert">{{ error() }}</p> }</div>
          </section>
          <aside class="panel info-panel"><h3>Privacy safeguard</h3>
            <p>Customer timeline shows only a safe progress update. Internal notes are available only in the authenticated Supervisor workspace.</p>
          </aside>
        </div>
      </div>
    }`,
})
export class SupervisorDetailPage {
  readonly id = input.required<string>();
  private readonly api = inject(SupervisorEscalationsApi);
  protected readonly escalation = signal<SupervisorEscalation | null>(null);
  protected readonly loading = signal(true);
  protected readonly loadError = signal('');
  protected readonly pending = signal(false);
  private acknowledgementRequestKey: string | null = null;
  private resolutionRequest: { key: string; note: string } | null = null;
  protected readonly history = computed<TimelineEvent[]>(() => (this.escalation()?.history ?? []).map(event => ({
    id: event.id,
    title: event.action === 'Created' ? 'Escalation created'
      : event.action === 'Acknowledged' ? 'Supervisor acknowledgement' : 'Escalation resolved',
    occurredAt: event.occurredAt,
    description: event.action === 'Created'
      ? 'Agent requested specialist review.'
      : event.privateNote ?? `${event.action} recorded by an authorized supervisor.`,
  })));
  protected readonly note = new FormControl('', { nonNullable: true });
  protected readonly error = signal('');
  protected readonly confirmation = signal('');

  constructor() {
    effect(() => {
      const id = this.id();
      this.note.reset();
      this.error.set('');
      this.confirmation.set('');
      this.acknowledgementRequestKey = null;
      this.resolutionRequest = null;
      this.escalation.set(null);
      void this.loadEscalation(id);
    });
  }

  protected async loadEscalation(id = this.id()): Promise<void> {
    this.loading.set(true);
    this.loadError.set('');
    try {
      this.escalation.set(await firstValueFrom(this.api.get(id)));
    } catch (error: unknown) {
      this.loadError.set(error instanceof HttpErrorResponse && error.status === 404
        ? 'This escalation could not be found in the Supervisor scope.'
        : 'Unable to load the escalation. Check your Supervisor session and retry.');
    } finally {
      this.loading.set(false);
    }
  }

  protected async acknowledge(): Promise<void> {
    // Retrying reuses the key so the server replays instead of repeating the action.
    this.acknowledgementRequestKey ??= crypto.randomUUID();
    this.pending.set(true);
    this.error.set('');
    try {
      await firstValueFrom(this.api.acknowledge(this.id(), this.acknowledgementRequestKey));
      this.acknowledgementRequestKey = null;
      this.confirmation.set('Escalation acknowledged. Record an internal resolution when your review is complete.');
      await this.loadEscalation();
    } catch {
      this.error.set('Unable to acknowledge the escalation. Retry to safely repeat the same request.');
    } finally {
      this.pending.set(false);
    }
  }

  protected async resolve(event: Event): Promise<void> {
    event.preventDefault();
    const note = this.note.value.trim().normalize('NFC');
    if (note.length < 15 || note.length > 2000) {
      this.error.set('Enter an internal resolution note between 15 and 2000 normalized characters.');
      return;
    }
    let request = this.resolutionRequest;
    if (request?.note !== note) {
      request = { key: crypto.randomUUID(), note };
      this.resolutionRequest = request;
    }
    this.pending.set(true);
    this.error.set('');
    try {
      await firstValueFrom(this.api.resolve(this.id(), note, request.key));
      this.resolutionRequest = null;
      this.note.reset();
      this.confirmation.set('Escalation resolved. The linked case remains under review; no funds are confirmed.');
      await this.loadEscalation();
    } catch {
      this.error.set('Unable to resolve the escalation. Retry to safely repeat the same request.');
    } finally {
      this.pending.set(false);
    }
  }
}
