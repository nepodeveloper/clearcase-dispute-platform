import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, ElementRef, inject, OnInit, signal, ViewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Subject, catchError, map, of, startWith, switchMap } from 'rxjs';
import { TransactionApi, TransactionPage, TransactionQuery, TransactionType } from './transaction-api';
import { transactionTypeLabel } from './transaction-type-label';
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
  imports: [ReactiveFormsModule, DecimalMoney, DatePipe, RouterLink],
  templateUrl: './transactions-page.html',
  styleUrl: './transactions-page.css',
})
export class TransactionsPage implements OnInit {
  private readonly api = inject(TransactionApi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly requests = new Subject<TransactionQuery>();
  @ViewChild('transactionsHeading') private transactionsHeading?: ElementRef<HTMLHeadingElement>;
  protected readonly state = signal<ViewState>({ kind: 'loading' });
  protected readonly dateRangeError = signal<string | null>(null);
  protected readonly filters = new FormGroup({
    transactionType: new FormControl<TransactionType | ''>('', { nonNullable: true }),
    fromDate: new FormControl('', { nonNullable: true }),
    toDate: new FormControl('', { nonNullable: true }),
  });
  private query: TransactionQuery = { pageNumber: 1, pageSize: 20, ...this.filters.getRawValue() };
  private focusAfterPageChange = false;

  ngOnInit(): void {
    this.filters.valueChanges.pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.dateRangeError.set(null));
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
      if (this.focusAfterPageChange && state.kind !== 'loading') {
        this.focusAfterPageChange = false;
        this.transactionsHeading?.nativeElement.focus();
      }
    });
    this.load();
  }

  protected applyFilters(): void {
    const filters = this.filters.getRawValue();
    if (filters.fromDate !== '' && filters.toDate !== '' && filters.fromDate > filters.toDate) {
      this.dateRangeError.set('To date must not precede from date.');
      return;
    }
    this.dateRangeError.set(null);
    this.focusAfterPageChange = false;
    this.query = { ...this.query, ...filters, pageNumber: 1 };
    this.load();
  }

  protected changePage(pageNumber: number): void {
    this.focusAfterPageChange = true;
    this.query = { ...this.query, pageNumber };
    this.load();
  }

  protected load(): void {
    this.requests.next(this.query);
  }

  protected readonly typeLabel = transactionTypeLabel;
}
