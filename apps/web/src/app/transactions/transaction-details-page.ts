import { CurrencyPipe, DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Subject, catchError, map, of, startWith, switchMap } from 'rxjs';
import { DisputeForm } from '../disputes/dispute-form';
import {
  DisputeEligibilityOutcome,
  TransactionApi,
  TransactionDetails,
  TransactionDisputeEligibility,
} from './transaction-api';
import { transactionTypeLabel } from './transaction-type-label';

type EligibilityState =
  | { kind: 'loading' }
  | { kind: 'error'; message: string; retryable: boolean }
  | { kind: 'loaded'; result: TransactionDisputeEligibility };

type ViewState =
  | { kind: 'loading' }
  | { kind: 'not-found' }
  | { kind: 'error'; message: string; retryable: boolean }
  | { kind: 'loaded'; transaction: TransactionDetails; eligibility: EligibilityState };

@Component({
  selector: 'app-transaction-details-page',
  imports: [CurrencyPipe, DatePipe, DisputeForm, RouterLink],
  templateUrl: './transaction-details-page.html',
  styleUrl: './transaction-details-page.css',
})
export class TransactionDetailsPage implements OnInit {
  private readonly api = inject(TransactionApi);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  private readonly retries = new Subject<void>();
  protected readonly state = signal<ViewState>({ kind: 'loading' });
  protected readonly typeLabel = transactionTypeLabel;

  ngOnInit(): void {
    this.route.paramMap.pipe(
      switchMap((params) => {
        const id = params.get('id');
        if (id === null) return of<ViewState>({ kind: 'not-found' });
        return this.retries.pipe(
          startWith(undefined),
          switchMap(() => this.api.getDetails(id).pipe(
            switchMap((transaction) => this.api.getDisputeEligibility(id).pipe(
              map((result): ViewState => ({
                kind: 'loaded',
                transaction,
                eligibility: { kind: 'loaded', result },
              })),
              catchError((error: unknown) => of<ViewState>({
                kind: 'loaded',
                transaction,
                eligibility: this.eligibilityError(error),
              })),
              startWith<ViewState>({
                kind: 'loaded',
                transaction,
                eligibility: { kind: 'loading' },
              }),
            )),
            catchError((error: unknown) => this.detailsError(error)),
            startWith<ViewState>({ kind: 'loading' }),
          )),
        );
      }),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe((state) => this.state.set(state));
  }

  protected retry(): void {
    this.retries.next();
  }

  protected eligibilityLabel(outcome: DisputeEligibilityOutcome): string {
    switch (outcome) {
      case 'EligibleToSubmit':
        return 'Eligible to submit a dispute.';
      case 'RequiresManualReview':
        return 'Requires further review.';
      case 'Ineligible':
        return 'Currently ineligible.';
    }
  }

  private eligibilityError(error: unknown): Extract<EligibilityState, { kind: 'error' }> {
    const retryable = error instanceof HttpErrorResponse
      && (error.status === 0 || error.status === 429 || error.status >= 500);
    return {
      kind: 'error',
      message: error instanceof HttpErrorResponse && error.status === 401
        ? 'Sign in again to check dispute eligibility.'
        : 'Unable to check dispute eligibility. Please try again later.',
      retryable,
    };
  }

  private detailsError(error: unknown) {
    if (error instanceof HttpErrorResponse && error.status === 404)
      return of<ViewState>({ kind: 'not-found' });
    const retryable = error instanceof HttpErrorResponse
      && (error.status === 0 || error.status === 429 || error.status >= 500);
    const message = error instanceof HttpErrorResponse && error.status === 401
      ? 'You are not signed in. Return to your bank to access transactions.'
      : error instanceof HttpErrorResponse && error.status === 403
        ? 'You do not have permission to view this transaction.'
        : error instanceof HttpErrorResponse && error.status === 400
          ? 'This transaction link is invalid.'
          : 'Unable to load transaction. Please try again later.';
    return of<ViewState>({ kind: 'error', message, retryable });
  }
}
