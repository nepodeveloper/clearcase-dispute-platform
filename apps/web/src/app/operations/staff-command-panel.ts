import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, input, output, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AgentDisputeDetails, AgentDisputesApi, EvidenceKind } from './agent-disputes-api';
import { StaffAction, toStaffAction } from './agent-dispute-commands';

@Component({
  selector: 'cc-staff-command-panel',
  template: `
    <section class="panel" aria-labelledby="staff-actions-heading">
      <div class="panel-title">
        <h2 id="staff-actions-heading">Case decisions &amp; processing</h2>
        <small>Version {{ dispute().version }}</small>
      </div>
      <p class="muted">Commands are authorized and validated on the server. This assessment uses mock financial adapters only.</p>
      @if (dispute().activeEscalation) {
        <p class="notice" role="status">Processing is paused while the Supervisor escalation is active.</p>
      } @else if (actions().length === 0) {
        <p class="muted" role="status">No staff command is currently available for this case. If the outcome is unknown, follow the supervisor or runbook procedure to reconcile before attempting another financial operation.</p>
      } @else {
        <div class="form-stack">
          @for (action of actions(); track action.command) {
            <button type="button" class="cc-button cc-button--secondary full"
              [disabled]="pending() || selected() !== null"
              (click)="choose(action)">{{ action.label }}</button>
          }
        </div>
      }
      @if (selected(); as action) {
        <div class="soft-box" role="group" aria-labelledby="staff-confirm-heading">
          <h3 id="staff-confirm-heading">Confirm: {{ action.label }}</h3>
          <p>{{ action.description }}</p>
          @if (action.confirmsMoney) {
            <p><strong>Mock operation only.</strong> No real money moves. A timeout or unknown outcome must not be retried blindly.</p>
          }
          @if (action.terminal) {
            <p>This records a decision or closure. Verify the case information first.</p>
          }
          @if (action.command === 'ReceiveRepresentment') {
            <label for="evidence-kind">Synthetic evidence type</label>
            <select id="evidence-kind" class="cc-control" [value]="evidenceKind()"
              [disabled]="pending()" (change)="selectEvidenceKind($event)">
              <option value="Receipt">Receipt</option>
              <option value="DeliveryConfirmation">Delivery confirmation</option>
              <option value="AuthorizationRecord">Authorization record</option>
            </select>
          }
          <div class="row-actions">
            <button type="button" class="cc-button cc-button--primary"
              [disabled]="pending()" (click)="execute()">
              {{ pending() ? 'Processing...' : 'Confirm ' + action.label }}
            </button>
            <button type="button" class="cc-button cc-button--secondary"
              [disabled]="pending()" (click)="cancel()">Cancel</button>
          </div>
        </div>
      }
      @if (error()) {
        <p class="cc-form-error" role="alert">{{ error() }}</p>
        @if (needsRefresh()) {
          <button class="cc-button cc-button--secondary" type="button"
            (click)="refreshRequested.emit()">Refresh current case</button>
        }
      }
    </section>
  `,
})
export class StaffCommandPanel {
  readonly dispute = input.required<AgentDisputeDetails>();
  readonly completed = output<string>();
  readonly refreshRequested = output<void>();
  private readonly api = inject(AgentDisputesApi);
  protected readonly actions = computed(() => this.dispute().availableActions.map(toStaffAction));
  protected readonly selected = signal<StaffAction | null>(null);
  protected readonly pending = signal(false);
  protected readonly error = signal('');
  protected readonly needsRefresh = signal(false);
  protected readonly evidenceKind = signal<EvidenceKind>('Receipt');

  protected selectEvidenceKind(event: Event): void {
    const target = event.target;
    if (!(target instanceof HTMLSelectElement)) return;
    const value = target.value;
    if (value === 'Receipt' || value === 'DeliveryConfirmation' || value === 'AuthorizationRecord')
      this.evidenceKind.set(value);
  }

  protected choose(action: StaffAction): void {
    if (this.pending()) return;
    this.selected.set(action);
    this.error.set('');
    this.needsRefresh.set(false);
  }

  protected cancel(): void {
    if (!this.pending()) {
      this.selected.set(null);
      this.error.set('');
    }
  }

  protected async execute(): Promise<void> {
    const action = this.selected();
    if (action === null || this.pending()) return;
    this.pending.set(true);
    this.error.set('');
    try {
      const evidence = action.command === 'ReceiveRepresentment'
        ? { id: crypto.randomUUID(), kind: this.evidenceKind() } : undefined;
      await firstValueFrom(this.api.command(this.dispute().id, action.command,
        this.dispute().version, evidence));
      this.selected.set(null);
      this.needsRefresh.set(false);
      this.completed.emit(action.label);
    } catch (failure: unknown) {
      if (failure instanceof HttpErrorResponse && failure.status === 409) {
        this.error.set('The case has changed or this action is no longer permitted. Refresh before attempting another decision.');
        this.needsRefresh.set(true);
        this.selected.set(null);
      } else if (failure instanceof HttpErrorResponse && failure.status === 503) {
        this.error.set('The financial processing adapter is unavailable. No successful outcome is assumed.');
        this.needsRefresh.set(true);
      } else if (failure instanceof HttpErrorResponse && [401, 403].includes(failure.status)) {
        this.error.set('Your session or staff authorization does not permit this action.');
        this.needsRefresh.set(true);
      } else {
        this.error.set('Unable to confirm the operation. Check the current case before retrying.');
        this.needsRefresh.set(true);
      }
    } finally {
      this.pending.set(false);
    }
  }
}
