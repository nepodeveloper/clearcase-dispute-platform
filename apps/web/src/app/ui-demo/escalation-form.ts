import { Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { DemoEscalation, ESCALATION_REASONS } from './demo-data';
import { DemoWorkspace } from './demo-workspace';

@Component({
  selector: 'cc-demo-escalation-form',
  imports: [ReactiveFormsModule],
  template: `
    <div class="modal-head"><span class="action-symbol" aria-hidden="true">&nearr;</span>
      <h2 id="escalation-heading">Escalate case</h2>
      <button class="icon-button" type="button" aria-label="Cancel escalation" (click)="cancelled.emit()">&times;</button>
    </div>
    <p class="muted">{{ dispute()?.reference }} &middot; Referred for specialist assessment</p>
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
          Submit demo escalation &rarr;
        </button>
      </div>
    </form>`,
})
export class DemoEscalationForm {
  readonly caseId = input.required<string>();
  readonly cancelled = output();
  readonly completed = output<DemoEscalation>();
  private readonly workspace = inject(DemoWorkspace);
  protected readonly dispute = computed(() => this.workspace.agentCase(this.caseId()));
  protected readonly reasons = ESCALATION_REASONS;
  protected readonly submitted = signal(false);
  protected readonly error = signal('');
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

  protected submit(): void {
    const { reason, note } = this.form.getRawValue();
    const result = this.workspace.escalate(this.caseId(), reason, note);
    if (!result.ok) { this.error.set(result.message); return; }
    this.submitted.set(true);
    this.completed.emit(result.value);
  }
}
