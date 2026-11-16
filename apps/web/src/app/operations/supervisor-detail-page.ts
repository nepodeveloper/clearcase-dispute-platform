import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { SupervisorEscalation, EscalationStatus } from '../supervisor/supervisor-escalations-api';
import { DemoEvent } from './demo-data';
import { DemoWorkspace } from './demo-workspace';
import { OPERATIONS_API } from './operations-api';
import { DemoHeading, DemoStatus, DemoTimeline } from './shared';

interface EscalationView {
  id: string;
  reference: string;
  caseId: string;
  reason: string;
  note?: string;
  status: EscalationStatus;
  createdAt: string;
  history: readonly DemoEvent[];
}

@Component({
  selector: 'cc-demo-supervisor-detail',
  host: { class: 'operations-content' },
  imports: [DatePipe, ReactiveFormsModule, RouterLink, DemoHeading, DemoStatus, DemoTimeline],
  template: `
    @if (operationsApi !== null && apiLoading() && apiEscalation() === null) {
      <p class="notice" role="status">Loading persisted escalation...</p>
    } @else if (apiError()) {
      <div class="cc-empty" role="alert"><h1>Escalation unavailable</h1><p>{{ apiError() }}</p>
        <button class="cc-button cc-button--secondary" type="button" (click)="loadApiEscalation()">Retry</button>
      </div>
    } @else if (escalation(); as item) {
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
              @if (apiEscalation(); as persisted) {
                <dt>Case status</dt><dd>{{ persisted.caseStatus }}</dd>
                <dt>Decision status</dt><dd>{{ persisted.decisionStatus }}</dd>
                <dt>Financial status</dt><dd>{{ persisted.financialStatus }}</dd>
              }
            </dl>
            <div class="soft-box"><small>Internal note</small><p>{{ item.note || 'No private note recorded.' }}</p></div>
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
              <button class="cc-button cc-button--secondary full" type="button" [disabled]="item.status !== 'Open' || pending()" (click)="acknowledge()">
                {{ item.status === 'Open' ? (operationsApi === null ? 'Acknowledge demo escalation' : 'Acknowledge escalation') : 'Acknowledged' }}
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
                  {{ operationsApi === null ? 'Resolve demo escalation' : 'Resolve escalation' }}
                </button>
              </form>
            } @else {
              <p class="notice">Specialist review is complete. The linked dispute remains under review.
                This action did not approve a refund or move money.</p>
            }
            <div id="resolution-error">@if (error()) { <p class="cc-form-error" role="alert">{{ error() }}</p> }</div>
          </section>
          <aside class="panel info-panel"><h3>Privacy safeguard</h3>
            <p>Customer timeline shows only a safe progress update. Internal notes are available only in the authenticated Supervisor workspace.
              @if (operationsApi === null) { These are synthetic fixtures, not real customer records or authentication. }</p>
          </aside>
        </div>
      </div>
    } @else {
      <div class="cc-empty"><h1 tabindex="-1">Escalation not found</h1><a routerLink="/agent/escalations">Back to escalations</a></div>
    }`,
})
export class DemoSupervisorDetailPage {
  readonly id = input.required<string>();
  private readonly workspace = inject(DemoWorkspace, { optional: true });
  protected readonly operationsApi = inject(OPERATIONS_API, { optional: true });
  protected readonly apiEscalation = signal<SupervisorEscalation | null>(null);
  protected readonly apiLoading = signal(false);
  protected readonly apiError = signal('');
  protected readonly pending = signal(false);
  private acknowledgementRequestKey: string | null = null;
  private resolutionRequest: { key: string; note: string } | null = null;
  protected readonly escalation = computed<EscalationView | undefined>(() => {
    if (this.operationsApi !== null) {
      const item = this.apiEscalation();
      if (item === null) return undefined;
      return {
        id: item.id, reference: item.reference, caseId: item.disputeId, reason: item.reason,
        note: item.note, status: item.status, createdAt: item.createdAt,
        history: item.history.map(event => ({
          id: event.id,
          title: event.action === 'Created' ? 'Escalation created'
            : event.action === 'Acknowledged' ? 'Supervisor acknowledgement' : 'Escalation resolved',
          occurredAt: event.occurredAt,
          description: event.action === 'Created'
            ? 'Agent requested specialist review.'
            : event.privateNote ?? `${event.action} recorded by an authorized supervisor.`,
        })),
      };
    }
    return this.workspace?.escalation(this.id());
  });
  protected readonly dispute = computed(() => {
    const escalation = this.escalation();
    if (escalation === undefined) return undefined;
    if (this.operationsApi !== null) {
      const item = this.apiEscalation();
      return item === null ? undefined : {
        reference: item.disputeReference,
        customerName: item.customerReference,
      };
    }
    return this.workspace?.agentCase(escalation.caseId);
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
      this.acknowledgementRequestKey = null;
      this.resolutionRequest = null;
      if (this.operationsApi !== null) void this.loadApiEscalation();
    });
  }

  protected async loadApiEscalation(): Promise<void> {
    if (this.operationsApi === null) return;
    this.apiLoading.set(true);
    this.apiError.set('');
    try {
      this.apiEscalation.set(await firstValueFrom(this.operationsApi.supervisor.get(this.id())));
    } catch (error: unknown) {
      this.apiError.set(error instanceof HttpErrorResponse && error.status === 404
        ? 'This escalation could not be found in the Supervisor scope.'
        : 'Unable to load the escalation. Check your Supervisor session and retry.');
    } finally {
      this.apiLoading.set(false);
    }
  }

  protected async acknowledge(): Promise<void> {
    if (this.operationsApi === null) {
      const result = this.workspace?.acknowledge(this.id());
      if (!result?.ok) { this.error.set(result?.message ?? 'Escalation not found.'); return; }
      this.error.set('');
      this.confirmation.set('Escalation acknowledged in this demo. Record an internal resolution when your review is complete.');
      return;
    }
    this.acknowledgementRequestKey ??= crypto.randomUUID();
    this.pending.set(true);
    this.error.set('');
    try {
      await firstValueFrom(this.operationsApi.supervisor.acknowledge(this.id(), this.acknowledgementRequestKey));
      this.acknowledgementRequestKey = null;
      this.confirmation.set('Escalation acknowledged. Record an internal resolution when your review is complete.');
      await this.loadApiEscalation();
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
    if (this.operationsApi === null) {
      const result = this.workspace?.resolve(this.id(), note);
      if (!result?.ok) { this.error.set(result?.message ?? 'Escalation not found.'); return; }
      this.note.reset();
      this.error.set('');
      this.confirmation.set('Demo escalation resolved. The customer sees a safe progress update; no funds are confirmed.');
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
      await firstValueFrom(this.operationsApi.supervisor.resolve(this.id(), note, request.key));
      this.resolutionRequest = null;
      this.note.reset();
      this.confirmation.set('Escalation resolved. The linked case remains under review; no funds are confirmed.');
      await this.loadApiEscalation();
    } catch {
      this.error.set('Unable to resolve the escalation. Retry to safely repeat the same request.');
    } finally {
      this.pending.set(false);
    }
  }
}
