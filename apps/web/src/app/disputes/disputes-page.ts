import { DatePipe } from '@angular/common';
import { Component, computed, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { transactionTypeLabel } from '../transactions/transaction-type-label';
import { StatCard, StatusBadge } from '../ui/ui-components';
import { DisputeApi, DisputeCaseStatus, DisputePage, DisputeQueryError, disputeQueryError } from './dispute-api';
import { disputeFinancialLabel, disputeStatusLabel } from './dispute-details';

type ViewState = { kind: 'loading' } | ({ kind: 'error' } & DisputeQueryError)
  | { kind: 'loaded'; page: DisputePage; loadingMore: boolean; error?: DisputeQueryError };

const finishedStatuses: readonly DisputeCaseStatus[] = ['Closed', 'Rejected', 'ReversalConfirmed'];

@Component({
  selector: 'app-disputes-page',
  imports: [DatePipe, RouterLink, StatCard, StatusBadge],
  templateUrl: './disputes-page.html',
})
export class DisputesPage implements OnInit {
  private readonly api = inject(DisputeApi);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly state = signal<ViewState>({ kind: 'loading' });
  private readonly loadedItems = computed(() => {
    const state = this.state();
    return state.kind === 'loaded' ? state.page.items : null;
  });
  protected readonly shown = computed(() => this.loadedItems()?.length);
  protected readonly finished = computed(() =>
    this.loadedItems()?.filter(item => finishedStatuses.includes(item.caseStatus)).length);
  protected readonly inProgress = computed(() =>
    this.loadedItems()?.filter(item => !finishedStatuses.includes(item.caseStatus)).length);
  protected readonly typeLabel = transactionTypeLabel;
  protected readonly statusLabel = disputeStatusLabel;
  protected readonly financialLabel = disputeFinancialLabel;

  ngOnInit(): void { this.load(); }

  protected load(cursor?: string): void {
    const previous = this.state();
    if (previous.kind === 'loaded' && previous.loadingMore) return;
    this.state.set(cursor !== undefined && previous.kind === 'loaded'
      ? { ...previous, loadingMore: true, error: undefined } : { kind: 'loading' });
    this.api.list(cursor).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (page) => this.state.set({
        kind: 'loaded', loadingMore: false,
        page: cursor !== undefined && previous.kind === 'loaded'
          ? { ...page, items: [...previous.page.items, ...page.items] } : page,
      }),
      error: (error: unknown) => this.state.set(cursor !== undefined && previous.kind === 'loaded'
        ? { ...previous, loadingMore: false, error: disputeQueryError(error, 'disputes') }
        : { kind: 'error', ...disputeQueryError(error, 'disputes') }),
    });
  }
}
