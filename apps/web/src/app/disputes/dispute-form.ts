import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { DisputeReason } from '../transactions/transaction-api';
import { Dispute, DisputeApi } from './dispute-api';

type SubmissionState =
  | { kind: 'editing' }
  | { kind: 'submitting' }
  | { kind: 'error'; message: string }
  | { kind: 'submitted'; dispute: Dispute };

export const descriptionMinLength = 15;
export const descriptionMaxLength = 2000;

@Component({
  selector: 'app-dispute-form',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './dispute-form.html',
  styleUrl: './dispute-form.css',
})
export class DisputeForm {
  readonly transactionId = input.required<string>();
  readonly reasons = input.required<DisputeReason[]>();

  private readonly api = inject(DisputeApi);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly state = signal<SubmissionState>({ kind: 'editing' });
  protected readonly serverErrors = signal<Partial<Record<'reasonCode' | 'description', string>>>({});
  protected readonly maxLength = descriptionMaxLength;
  protected readonly form = inject(NonNullableFormBuilder).group({
    reasonCode: ['', Validators.required],
    description: ['', [
      Validators.required,
      Validators.minLength(descriptionMinLength),
      Validators.maxLength(descriptionMaxLength),
    ]],
  });

  constructor() {
    this.form.valueChanges.pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.serverErrors.set({}));
  }

  protected submit(): void {
    if (this.state().kind === 'submitting' || this.state().kind === 'submitted') return;
    const value = this.form.getRawValue();
    const description = value.description.trim();
    this.form.controls.description.setValue(description);
    this.form.markAllAsTouched();
    if (this.form.invalid) return;

    this.state.set({ kind: 'submitting' });
    this.api.submit({ transactionId: this.transactionId(), reasonCode: value.reasonCode, description })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (dispute) => {
          this.state.set({ kind: 'submitted', dispute });
          void this.router.navigate(['/disputes', dispute.id]);
        },
        error: (error: unknown) => {
          this.readFieldErrors(error);
          this.state.set({ kind: 'error', message: this.errorMessage(error) });
        },
      });
  }

  protected showError(control: 'reasonCode' | 'description'): boolean {
    const field = this.form.controls[control];
    return (field.invalid && field.touched) || this.serverErrors()[control] !== undefined;
  }

  private readFieldErrors(error: unknown): void {
    const body: unknown = error instanceof HttpErrorResponse ? error.error : null;
    if (typeof body !== 'object' || body === null || !('errors' in body) || !Array.isArray(body.errors)) return;
    const fields: Partial<Record<'reasonCode' | 'description', string>> = {};
    const entries: unknown[] = body.errors;
    for (const entry of entries) {
      if (typeof entry !== 'object' || entry === null || !('pointer' in entry)
        || !('message' in entry) || typeof entry.message !== 'string') continue;
      if (entry.pointer === '/reasonCode') fields.reasonCode = entry.message;
      if (entry.pointer === '/description') fields.description = entry.message;
    }
    this.serverErrors.set(fields);
  }

  private errorMessage(error: unknown): string {
    if (!(error instanceof HttpErrorResponse)) return 'Unable to submit your dispute. Please try again later.';
    const detail: unknown = error.error?.detail;
    switch (error.status) {
      case 409:
        return 'This transaction already has an open dispute.';
      case 422:
        return typeof detail === 'string' && detail !== ''
          ? detail
          : 'This dispute cannot be submitted. Check the reason and try again.';
      case 400:
        return 'Check the dispute details and try again.';
      case 401:
        return 'Sign in again to submit a dispute.';
      case 403:
        return 'You do not have permission to submit this dispute.';
      default:
        return 'Unable to submit your dispute. Please try again later.';
    }
  }
}
