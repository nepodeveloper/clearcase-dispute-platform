import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { afterNextRender, Component, computed, DestroyRef, ElementRef, inject, Injector, OnInit, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Subscription } from 'rxjs';
import { DateField } from '../ui/date-field';
import { createFilterDebouncer } from '../ui/filter-debounce';
import { defaultPageSize, PageSize, PageSizeSelect, StatCard, StatusBadge } from '../ui/ui-components';
import { Transaction, TransactionApi, TransactionPage, TransactionQuery, TransactionType } from './transaction-api';
import { transactionStatusLabel, transactionTypeLabel } from './transaction-type-label';
import { DecimalMoney } from './decimal-money';

type ViewState =
  | { kind: 'loading' }
  | { kind: 'error'; message: string; retryable: boolean }
  | { kind: 'invalid'; messages: string[] }
  | { kind: 'loaded' };

type MoreError = { message: string; reload: boolean };

function validationMessages(error: HttpErrorResponse): string[] {
  const body: unknown = error.error;
  if (typeof body !== 'object' || body === null || !('errors' in body) || !Array.isArray(body.errors))
    return [];
  const errors: unknown[] = body.errors;
  return errors.flatMap((entry) =>
    typeof entry === 'object' && entry !== null && 'message' in entry && typeof entry.message === 'string'
      ? [entry.message]
      : []);
}

function loadFailure(error: unknown): ViewState {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 400) {
      const messages = validationMessages(error);
      if (messages.length > 0) return { kind: 'invalid', messages };
      return { kind: 'error', message: 'Check the transaction filters and try again.', retryable: false };
    }
    const retryable = error.status === 0 || error.status === 429 || error.status >= 500;
    return {
      kind: 'error',
      message: retryable
        ? 'Unable to load transactions. Please try again.'
        : 'Unable to access transactions. Please check your session.',
      retryable,
    };
  }
  return { kind: 'error', message: 'Unable to load transactions.', retryable: false };
}

@Component({
  selector: 'app-transactions-page',
  imports: [ReactiveFormsModule, DateField, DecimalMoney, DatePipe, RouterLink, PageSizeSelect, StatCard, StatusBadge],
  templateUrl: './transactions-page.html',
})
export class TransactionsPage implements OnInit {
  private readonly api = inject(TransactionApi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly heading = viewChild<ElementRef<HTMLHeadingElement>>('transactionsHeading');
  private readonly status = viewChild<ElementRef<HTMLElement>>('transactionsStatus');
  private readonly loadMoreButton = viewChild<ElementRef<HTMLButtonElement>>('loadMoreButton');
  protected readonly state = signal<ViewState>({ kind: 'loading' });
  protected readonly rows = signal<Transaction[]>([]);
  protected readonly nextCursor = signal<string | undefined>(undefined);
  protected readonly loadingMore = signal(false);
  protected readonly moreError = signal<MoreError | null>(null);
  protected readonly pageSize = signal<PageSize>(defaultPageSize);
  private readonly total = signal<number | undefined>(undefined);
  protected readonly hasMore = computed(() => this.nextCursor() !== undefined);
  protected readonly loadMoreBlocked = computed(() => this.loadingMore() || this.state().kind === 'loading');
  protected readonly totalCount = computed(() => this.total() ?? (this.state().kind === 'loaded'
    ? `${this.rows().length}${this.hasMore() ? '+' : ''}` : '\u2013'));
  protected readonly shownCount = computed(() => this.rows().length === 0 && this.state().kind !== 'loaded'
    ? '\u2013' : this.rows().length);
  protected readonly pageSummary = computed(() => this.state().kind !== 'loaded' && this.rows().length === 0
    ? 'Loading' : this.hasMore() ? 'More available' : 'All matching loaded');
  protected readonly countSummary = computed(() => {
    if (this.state().kind === 'loading' && this.rows().length > 0) return 'Updating results\u2026';
    if (this.state().kind !== 'loaded') return '';
    const count = this.rows().length;
    const total = this.total();
    if (total !== undefined) return `Showing ${count} of ${total} transactions`;
    return `Showing ${count}${this.hasMore() ? '+' : ''} ${count === 1 && !this.hasMore() ? 'transaction' : 'transactions'}`;
  });
  protected readonly dateError = signal<{ field: 'fromDate' | 'toDate'; message: string } | null>(null);
  protected readonly filters = new FormGroup({
    search: new FormControl('', { nonNullable: true }),
    transactionType: new FormControl<TransactionType | ''>('', { nonNullable: true }),
    fromDate: new FormControl('', { nonNullable: true }),
    toDate: new FormControl('', { nonNullable: true }),
  });
  private query: TransactionQuery = { pageSize: defaultPageSize, ...this.filters.getRawValue() };
  private focusAfterLoad = false;
  private request: Subscription | null = null;
  private lastSearchText = '';
  private readonly filterDebounce = createFilterDebouncer(this.destroyRef, () => this.applyFilters(false));

  ngOnInit(): void {
    this.filters.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(value => {
      this.dateError.set(null);
      const search = value.search ?? '';
      if (search !== this.lastSearchText) {
        this.lastSearchText = search;
        this.filterDebounce.schedule();
      } else {
        this.filterDebounce.now();
      }
    });
    this.destroyRef.onDestroy(() => this.request?.unsubscribe());
    this.load();
  }


  protected applyFilters(explicit = true): void {
    this.filterDebounce.cancel();
    const unreadable = (['fromDate', 'toDate'] as const).find(field => this.filters.controls[field].invalid);
    if (unreadable !== undefined) {
      if (explicit)
        this.dateError.set({ field: unreadable, message: 'Enter dates as dd MMM yyyy, for example 01 Nov 2026.' });
      return;
    }
    const filters = this.filters.getRawValue();
    if (filters.fromDate !== '' && filters.toDate !== '' && filters.fromDate > filters.toDate) {
      this.dateError.set({ field: 'toDate', message: 'To date must not precede from date.' });
      return;
    }
    this.dateError.set(null);
    const next = { ...filters, search: filters.search.trim() };
    if (!explicit && next.search === this.query.search && next.transactionType === this.query.transactionType
      && next.fromDate === this.query.fromDate && next.toDate === this.query.toDate) return;
    this.focusAfterLoad = false;
    this.query = { ...this.query, ...next };
    this.load();
  }

  protected enterPressed(event: Event): void {
    if (!(event.target instanceof HTMLInputElement)) return;
    event.preventDefault();
    this.applyFilters(true);
  }

  protected changePageSize(pageSize: PageSize): void {
    this.focusAfterLoad = true;
    this.pageSize.set(pageSize);
    this.query = { ...this.query, pageSize };
    this.load();
  }

  protected load(): void {
    this.request?.unsubscribe();
    this.loadingMore.set(false);
    this.moreError.set(null);
    this.state.set({ kind: 'loading' });
    this.request = this.api.list({ ...this.query, cursor: undefined }).subscribe({
      next: (page) => {
        this.accept(page, false);
        this.state.set({ kind: 'loaded' });
        this.restoreFocus();
      },
      error: (error: unknown) => {
        this.state.set(loadFailure(error));
        this.restoreFocus();
      },
    });
  }

  protected loadMore(): void {
    const cursor = this.nextCursor();
    if (cursor === undefined || this.loadMoreBlocked()) return;
    const hadFocus = document.activeElement === this.loadMoreButton()?.nativeElement;
    this.loadingMore.set(true);
    this.moreError.set(null);
    this.request?.unsubscribe();
    this.request = this.api.list({ ...this.query, cursor }).subscribe({
      next: (page) => {
        this.accept(page, true);
        this.loadingMore.set(false);
        if (hadFocus && !this.hasMore())
          afterNextRender(() => this.status()?.nativeElement.focus(), { injector: this.injector });
      },
      error: (error: unknown) => {
        this.loadingMore.set(false);
        this.moreError.set(error instanceof HttpErrorResponse && error.status === 400
          ? { message: 'The list changed while you were browsing. Reload it to continue.', reload: true }
          : { message: 'Unable to load more transactions. Your existing results are still shown.', reload: false });
      },
    });
  }

  private accept(page: TransactionPage, append: boolean): void {
    this.rows.update(rows => append ? [...rows, ...page.items] : page.items);
    this.nextCursor.set(page.pageInfo.hasNextPage ? page.pageInfo.nextCursor : undefined);
    if (page.totalCount !== undefined || !append) this.total.set(page.totalCount);
  }

  private restoreFocus(): void {
    if (this.focusAfterLoad) this.heading()?.nativeElement.focus();
    this.focusAfterLoad = false;
  }

  protected readonly typeLabel = transactionTypeLabel;
  protected readonly statusLabel = transactionStatusLabel;
}
