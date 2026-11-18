import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, DestroyRef, ElementRef, inject, OnInit, signal, ViewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Subject, catchError, map, of, startWith, switchMap } from 'rxjs';
import { DateField } from '../ui/date-field';
import { defaultPageSize, PageSize, PageSizeSelect, StatCard, StatusBadge } from '../ui/ui-components';
import { TransactionApi, TransactionPage, TransactionQuery, TransactionType } from './transaction-api';
import { transactionStatusLabel, transactionTypeLabel } from './transaction-type-label';
import { DecimalMoney } from './decimal-money';

type ViewState =
  | { kind: 'loading' }
  | { kind: 'error'; message: string; retryable: boolean }
  | { kind: 'invalid'; messages: string[] }
  | { kind: 'loaded'; page: TransactionPage };

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

@Component({
  selector: 'app-transactions-page',
  imports: [ReactiveFormsModule, DateField, DecimalMoney, DatePipe, RouterLink, PageSizeSelect, StatCard, StatusBadge],
  templateUrl: './transactions-page.html',
})
export class TransactionsPage implements OnInit {
  private readonly api = inject(TransactionApi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly requests = new Subject<TransactionQuery>();
  @ViewChild('transactionsHeading') private transactionsHeading?: ElementRef<HTMLHeadingElement>;
  protected readonly state = signal<ViewState>({ kind: 'loading' });
  // Keeps the summary cards steady while the next page or filter result is loading.
  private readonly lastPage = signal<TransactionPage | null>(null);
  protected readonly totalCount = computed(() => this.lastPage()?.totalCount ?? '\u2013');
  protected readonly shownCount = computed(() => this.lastPage()?.items.length ?? '\u2013');
  protected readonly pageSummary = computed(() => {
    const page = this.lastPage();
    return page === null ? 'Loading' : `Page ${page.pageNumber} of ${Math.max(page.totalPages, 1)}`;
  });
  protected readonly dateError = signal<{ field: 'fromDate' | 'toDate'; message: string } | null>(null);
  protected readonly filters = new FormGroup({
    search: new FormControl('', { nonNullable: true }),
    transactionType: new FormControl<TransactionType | ''>('', { nonNullable: true }),
    fromDate: new FormControl('', { nonNullable: true }),
    toDate: new FormControl('', { nonNullable: true }),
  });
  private query: TransactionQuery = { pageNumber: 1, pageSize: defaultPageSize, ...this.filters.getRawValue() };
  private focusAfterPageChange = false;

  ngOnInit(): void {
    this.filters.valueChanges.pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.dateError.set(null));
    this.requests.pipe(
      switchMap((query) => this.api.list(query).pipe(
        map((page): ViewState => ({ kind: 'loaded', page })),
        catchError((error: unknown) => {
          if (error instanceof HttpErrorResponse) {
            if (error.status === 400) {
              const messages = validationMessages(error);
              if (messages.length > 0) return of<ViewState>({ kind: 'invalid', messages });
              return of<ViewState>({ kind: 'error', message: 'Check the transaction filters and try again.', retryable: false });
            }
            const retryable = error.status === 0 || error.status === 429 || error.status >= 500;
            return of<ViewState>({
              kind: 'error',
              message: retryable
                ? 'Unable to load transactions. Please try again.'
                : 'Unable to access transactions. Please check your session.',
              retryable,
            });
          }
          return of<ViewState>({ kind: 'error', message: 'Unable to load transactions.', retryable: false });
        }),
        startWith<ViewState>({ kind: 'loading' }),
      )),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe((state) => {
      this.state.set(state);
      if (state.kind === 'loaded') this.lastPage.set(state.page);
      if (this.focusAfterPageChange && state.kind !== 'loading') {
        this.focusAfterPageChange = false;
        this.transactionsHeading?.nativeElement.focus();
      }
    });
    this.load();
  }

  protected applyFilters(): void {
    const unreadable = (['fromDate', 'toDate'] as const).find(field => this.filters.controls[field].invalid);
    if (unreadable !== undefined) {
      this.dateError.set({ field: unreadable, message: 'Enter dates as dd MMM yyyy, for example 01 Nov 2026.' });
      return;
    }
    const filters = this.filters.getRawValue();
    if (filters.fromDate !== '' && filters.toDate !== '' && filters.fromDate > filters.toDate) {
      this.dateError.set({ field: 'toDate', message: 'To date must not precede from date.' });
      return;
    }
    this.dateError.set(null);
    this.focusAfterPageChange = false;
    this.query = { ...this.query, ...filters, search: filters.search.trim(), pageNumber: 1 };
    this.load();
  }

  protected changePage(pageNumber: number): void {
    this.focusAfterPageChange = true;
    this.query = { ...this.query, pageNumber };
    this.load();
  }

  protected changePageSize(pageSize: PageSize): void {
    this.focusAfterPageChange = true;
    this.query = { ...this.query, pageSize, pageNumber: 1 };
    this.load();
  }

  protected load(): void {
    this.requests.next(this.query);
  }

  protected readonly typeLabel = transactionTypeLabel;
  protected readonly statusLabel = transactionStatusLabel;
}
