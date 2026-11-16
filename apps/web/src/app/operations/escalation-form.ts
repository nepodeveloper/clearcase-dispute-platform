import { Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { AgentActiveEscalation } from '../agent/agent-disputes-api';
import { DemoEscalation, ESCALATION_REASONS } from './demo-data';
import { DemoWorkspace } from './demo-workspace';
import { OPERATIONS_API } from './operations-api';

@Component({
  selector: 'cc-demo-escalation-form',
  imports: [ReactiveFormsModule],
  template: `
    <div class="modal-head"><span class="action-symbol" aria-hidden="true">&nearr;</span>
      <h2 id="escalation-heading">Escalate case</h2>
      <button class="icon-button" type="button" aria-label="Cancel escalation" (click)="cancelled.emit()">&times;</button>
    </div>
    <p class="muted">{{ caseReference() }} &middot; Referred for specialist assessment</p>
    <form class="form-stack" [formGroup]="form" (ngSubmit)="submit()" novalidate>
      <div class="cc-field"><label for="escalation-reason">Escalation reason <span class="required">*</span></label>
        <select class="cc-control" id="escalation-reason" formControlName="reason" required autofocus>
          @for (reason of reasons; track reason) { <option [value]="reason">{{ reason }}</option> }
        </select>
      </div>
      <div class="cc-field"><label for="escalation-note">Internal investigation note <span class="required">*</span></label>
        <textarea class="cc-control" id="escalation-note" formControlName="note" rows="5" maxlength="2000" required
          placeholder="Explain why this case needs specialist review."
          [attr.aria-invalid]="error() !== ''" aria-describedby="escalation-hint escalation-error"></textarea>
        <span class="field-hint" id="escalation-hint">Internal only &middot; Not shown to the customer. 15&ndash;2000 characters.</span>
      </div>
      <div id="escalation-error">@if (error()) { <p class="cc-form-error" role="alert">{{ error() }}</p> }</div>
      <div class="button-row align-right">
        <button class="cc-button cc-button--secondary" type="button" (click)="cancelled.emit()">Cancel</button>
        <button class="cc-button cc-button--primary" type="submit" [disabled]="submitted()">
          {{ operationsApi === null ? 'Submit demo escalation' : 'Submit escalation' }} &rarr;
        </button>
      </div>
    </form>`,
})
export class DemoEscalationForm {
  readonly caseId = input.required<string>();
  readonly reference = input<string | null>(null);
  readonly cancelled = output();
  readonly completed = output<DemoEscalation | AgentActiveEscalation>();
  private readonly workspace = inject(DemoWorkspace, { optional: true });
  protected readonly operationsApi = inject(OPERATIONS_API, { optional: true });
  protected readonly caseReference = computed(() => this.reference() ??
    this.workspace?.agentCase(this.caseId())?.reference ?? this.caseId());
  protected readonly reasons = ESCALATION_REASONS;
  protected readonly submitted = signal(false);
  protected readonly error = signal('');
  private request: { key: string; payload: string } | null = null;
  protected readonly form = new FormGroup({
    reason: new FormControl<string>(ESCALATION_REASONS[0], { nonNullable: true }),
    note: new FormControl('', { nonNullable: true }),
  });

  constructor() {
    effect(() => {
      this.caseId();
      this.form.reset({ reason: ESCALATION_REASONS[0], note: '' });
      this.error.set('');
      this.submitted.set(false);
    });
  }

  protected async submit(): Promise<void> {
    const { reason, note } = this.form.getRawValue();
    const normalizedNote = note.trim().normalize('NFC');
    if (normalizedNote.length < 15 || normalizedNote.length > 2000) {
      this.error.set('Enter an internal note between 15 and 2000 normalized characters.');
      return;
    }
    if (this.operationsApi === null) {
      const result = this.workspace?.escalate(this.caseId(), reason, normalizedNote);
      if (!result?.ok) { this.error.set(result?.message ?? 'Case not found.'); return; }
      this.submitted.set(true);
      this.completed.emit(result.value);
      return;
    }
    const payload = JSON.stringify({ reason, note: normalizedNote });
    let request = this.request;
    if (request?.payload !== payload) {
      request = { key: crypto.randomUUID(), payload };
      this.request = request;
    }
    this.submitted.set(true);
    this.error.set('');
    try {
      const result = await firstValueFrom(this.operationsApi.agent.createEscalation(
        this.caseId(), reason, normalizedNote, request.key,
      ));
      this.request = null;
      this.completed.emit(result);
    } catch (error: unknown) {
      this.error.set(error instanceof HttpErrorResponse && error.status === 409
        ? 'This case already has an active escalation or changed since it was loaded.'
        : 'Unable to save the escalation. Retry to safely repeat the same request.');
      this.submitted.set(false);
    }
  }
}
