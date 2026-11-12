import { CurrencyPipe, DatePipe } from '@angular/common';
import { Component, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { Subject, catchError, map, of, startWith, switchMap } from 'rxjs';
import { TransactionApi, TransactionPage, TransactionQuery, TransactionType } from './transaction-api';

type ViewState =
  | { kind: 'loading' }
  | { kind: 'error' }
  | { kind: 'loaded'; page: TransactionPage };

@Component({
  selector: 'app-transactions-page',
  imports: [ReactiveFormsModule, CurrencyPipe, DatePipe],
  templateUrl: './transactions-page.html',
  styleUrl: './transactions-page.css',
})
export class TransactionsPage implements OnInit {
  private readonly api = inject(TransactionApi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly requests = new Subject<TransactionQuery>();
  protected readonly state = signal<ViewState>({ kind: 'loading' });
  protected readonly filters = new FormGroup({
    transactionType: new FormControl<TransactionType | ''>('', { nonNullable: true }),
    fromDate: new FormControl('', { nonNullable: true }),
    toDate: new FormControl('', { nonNullable: true }),
  });
  private query: TransactionQuery = { pageNumber: 1, pageSize: 20, ...this.filters.getRawValue() };

  ngOnInit(): void {
    this.requests.pipe(
      switchMap((query) => this.api.list(query).pipe(
        map((page): ViewState => ({ kind: 'loaded', page })),
        catchError(() => of<ViewState>({ kind: 'error' })),
        startWith<ViewState>({ kind: 'loading' }),
      )),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe((state) => this.state.set(state));
    this.load();
  }

  protected applyFilters(): void {
    this.query = { ...this.query, ...this.filters.getRawValue(), pageNumber: 1 };
    this.load();
  }

  protected changePage(pageNumber: number): void {
    this.query = { ...this.query, pageNumber };
    this.load();
  }

  protected load(): void {
    this.requests.next(this.query);
  }

  protected typeLabel(type: TransactionType): string {
    return { CardPurchase: 'Card purchase', DebitOrder: 'Debit order', BankCharge: 'Bank charge' }[type];
  }
}
