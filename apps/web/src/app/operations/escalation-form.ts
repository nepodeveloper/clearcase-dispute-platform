import { HttpErrorResponse } from '@angular/common/http';
import { Component, effect, inject, input, output, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { AgentEscalationCreation, AgentDisputesApi } from './agent-disputes-api';

export const ESCALATION_REASONS = [
  'Additional investigation required', 'Customer authorization dispute',
  'Mandate mismatch', 'Policy exception review',
] as const;

@Component({
  selector: 'cc-escalation-form',
  imports: [ReactiveFormsModule],
  template: `
    <div class="modal-head"><span class="action-symbol" aria-hidden="true">&nearr;</span>
      <h2 id="escalation-heading">Escalate case</h2>
      <button class="icon-button" type="button" aria-label="Cancel escalation" (click)="cancelled.emit()">&times;</button>
    </div>
    <p class="muted">{{ reference() }} &middot; Referred for specialist assessment</p>
    <form class="form-stack" [formGroup]="form" (ngSubmit)="submit()" novalidate>
      <div class="cc-field"><label for="escalation-reason">Escalation reason <span class="required">*</span></label>
        <!-- eslint-disable-next-line @angular-eslint/template/no-autofocus -- focuses the first field when the modal opens -->
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
        <button class="cc-button cc-button--primary" type="submit" [disabled]="submitting()">Submit escalation &rarr;</button>
      </div>
    </form>`,
})
export class EscalationForm {
  readonly caseId = input.required<string>();
  readonly reference = input.required<string>();
  readonly cancelled = output();
  readonly completed = output<AgentEscalationCreation>();
  private readonly api = inject(AgentDisputesApi);
  protected readonly reasons = ESCALATION_REASONS;
  protected readonly submitting = signal(false);
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
      this.submitting.set(false);
      this.request = null;
    });
  }

  protected async submit(): Promise<void> {
    if (this.submitting()) return;
    const { reason, note } = this.form.getRawValue();
    const normalizedNote = note.trim().normalize('NFC');
    if (normalizedNote.length < 15 || normalizedNote.length > 2000) {
      this.error.set('Enter an internal note between 15 and 2000 normalized characters.');
      return;
    }
    // Retrying an unchanged request reuses its key so the server replays instead of duplicating.
    const payload = JSON.stringify({ reason, note: normalizedNote });
    let request = this.request;
    if (request?.payload !== payload) {
      request = { key: crypto.randomUUID(), payload };
      this.request = request;
    }
    this.submitting.set(true);
    this.error.set('');
    try {
      const result = await firstValueFrom(this.api.createEscalation(this.caseId(), reason, normalizedNote, request.key));
      this.request = null;
      this.completed.emit(result);
    } catch (error: unknown) {
      this.error.set(error instanceof HttpErrorResponse && error.status === 409
        ? 'This case already has an active escalation or changed since it was loaded.'
        : 'Unable to save the escalation. Retry to safely repeat the same request.');
      this.submitting.set(false);
    }
  }
}
