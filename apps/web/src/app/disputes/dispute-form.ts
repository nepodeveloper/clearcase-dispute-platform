import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { DisputeReason } from '../transactions/transaction-api';
import { Dispute, DisputeApi, DisputeCaseStatus } from './dispute-api';

type SubmissionState =
  | { kind: 'editing' }
  | { kind: 'submitting' }
  | { kind: 'error'; message: string }
  | { kind: 'submitted'; dispute: Dispute };

export const descriptionMinLength = 15;
export const descriptionMaxLength = 2000;

@Component({
  selector: 'app-dispute-form',
  imports: [ReactiveFormsModule],
  templateUrl: './dispute-form.html',
  styleUrl: './dispute-form.css',
})
export class DisputeForm {
  readonly transactionId = input.required<string>();
  readonly reasons = input.required<DisputeReason[]>();

  private readonly api = inject(DisputeApi);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly state = signal<SubmissionState>({ kind: 'editing' });
  protected readonly refreshing = signal(false);
  protected readonly refreshError = signal<string | null>(null);
  protected readonly maxLength = descriptionMaxLength;
  protected readonly form = inject(NonNullableFormBuilder).group({
    reasonCode: ['', Validators.required],
    description: ['', [
      Validators.required,
      Validators.minLength(descriptionMinLength),
      Validators.maxLength(descriptionMaxLength),
    ]],
  });

  protected submit(): void {
    if (this.state().kind === 'submitting') return;
    const value = this.form.getRawValue();
    const description = value.description.trim();
    this.form.controls.description.setValue(description);
    this.form.markAllAsTouched();
    if (this.form.invalid) return;

    this.state.set({ kind: 'submitting' });
    this.api.submit({ transactionId: this.transactionId(), reasonCode: value.reasonCode, description })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (dispute) => this.state.set({ kind: 'submitted', dispute }),
        error: (error: unknown) => this.state.set({ kind: 'error', message: this.errorMessage(error) }),
      });
  }

  protected refresh(): void {
    const current = this.state();
    if (current.kind !== 'submitted' || this.refreshing()) return;
    this.refreshing.set(true);
    this.refreshError.set(null);
    this.api.get(current.dispute.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (dispute) => {
        this.state.set({ kind: 'submitted', dispute });
        this.refreshing.set(false);
      },
      error: () => {
        this.refreshError.set('Unable to refresh the case status. Please try again.');
        this.refreshing.set(false);
      },
    });
  }

  protected statusLabel(status: DisputeCaseStatus): string {
    switch (status) {
      case 'Submitted':
        return 'Submitted - awaiting review';
      case 'ManualReview':
        return 'Referred for specialist review';
      case 'Closed':
        return 'Closed';
      case 'UnderReview':
        return 'Under review';
      case 'MandateVerified':
        return 'Mandate verified';
      case 'ReversalRequested':
        return 'Reversal requested';
      case 'ReversalConfirmed':
        return 'Simulated reversal confirmed';
      case 'Rejected':
        return 'Rejected';
    }
  }

  protected financialLabel(status: string): string {
    switch (status) {
      case 'NotInitiated': return 'Not initiated';
      case 'Requested': return 'Requested - not confirmed';
      case 'OutcomeUnknown': return 'Outcome unknown - not confirmed';
      case 'Failed': return 'Failed - no reversal confirmed';
      case 'Confirmed': return 'Simulated reversal confirmed - no real money moved';
      default: return status;
    }
  }

  protected showError(control: 'reasonCode' | 'description'): boolean {
    const field = this.form.controls[control];
    return field.invalid && field.touched;
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
      default:
        return 'Unable to submit your dispute. Please try again later.';
    }
  }
}
