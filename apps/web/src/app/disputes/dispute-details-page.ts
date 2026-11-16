import { DOCUMENT } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, ElementRef, inject, OnInit, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { catchError, filter, interval, map, merge, of, startWith, Subject, switchMap } from 'rxjs';
import { transactionTypeLabel } from '../transactions/transaction-type-label';
import { StatusBadge } from '../ui/ui-components';
import { Dispute, DisputeApi, DisputeQueryError, disputeQueryError } from './dispute-api';
import { DisputeDetails, disputeStatusLabel } from './dispute-details';

type ViewState = { kind: 'loading' } | { kind: 'not-found' }
  | ({ kind: 'error' } & DisputeQueryError)
  | { kind: 'loaded'; dispute: Dispute; refreshing: boolean; error?: DisputeQueryError };

@Component({
  selector: 'app-dispute-details-page',
  imports: [DisputeDetails, RouterLink, StatusBadge],
  templateUrl: './dispute-details-page.html',
})
export class DisputeDetailsPage implements OnInit {
  private readonly api = inject(DisputeApi);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  private readonly document = inject(DOCUMENT);
  private readonly refreshes = new Subject<void>();
  private readonly heading = viewChild<ElementRef<HTMLHeadingElement>>('heading');
  private focusOnResponse = false;
  protected readonly state = signal<ViewState>({ kind: 'loading' });
  protected readonly typeLabel = transactionTypeLabel;
  protected readonly statusLabel = disputeStatusLabel;

  ngOnInit(): void {
    this.route.paramMap.pipe(
      switchMap((params) => {
        const id = params.get('id');
        if (id === null) return of<ViewState>({ kind: 'not-found' });
        return merge(this.refreshes.pipe(map(() => true)), interval(30_000).pipe(
          filter(() => {
            const state = this.state();
            return this.document.visibilityState === 'visible' && state.kind === 'loaded'
              && !state.refreshing && state.dispute.caseStatus !== 'Closed';
          }),
          map(() => false),
        )).pipe(startWith(true), switchMap(focus => {
          this.focusOnResponse = focus;
          const current = this.state();
          const previous = current.kind === 'loaded' && current.dispute.id === id ? current : null;
          return this.api.get(id).pipe(
            map((dispute): ViewState => ({ kind: 'loaded', dispute, refreshing: false })),
            catchError((error: unknown) => {
              const failure = disputeQueryError(error, 'dispute');
              return of<ViewState>(previous !== null && failure.retryable
                ? { ...previous, refreshing: false, error: failure }
                : error instanceof HttpErrorResponse && error.status === 404
                  ? { kind: 'not-found' } : { kind: 'error', ...failure });
            }),
            startWith<ViewState>(previous === null ? { kind: 'loading' }
              : { ...previous, refreshing: true, error: undefined }),
          );
        }));
      }),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe((state) => {
      this.state.set(state);
      if (this.focusOnResponse && state.kind !== 'loading' && !(state.kind === 'loaded' && state.refreshing)) {
        this.focusOnResponse = false;
        this.heading()?.nativeElement.focus();
      }
    });
  }

  protected refresh(): void {
    const state = this.state();
    if (state.kind !== 'loaded' || !state.refreshing) this.refreshes.next();
  }
}
