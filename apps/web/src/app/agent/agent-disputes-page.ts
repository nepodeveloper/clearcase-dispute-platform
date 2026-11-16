import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, ElementRef, inject, OnInit, signal, ViewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Subject, catchError, map, of, startWith, switchMap } from 'rxjs';
import { DecimalMoney } from '../transactions/decimal-money';
import {
  AgentCaseStatus,
  AgentDisputeCategory,
  AgentDisputesApi,
  AgentDisputeFilters,
  AgentDisputePage,
  decimalFromMinorUnits,
} from './agent-disputes-api';

type QueueState =
  | { kind: 'loading' }
  | { kind: 'error'; message: string; retryable: boolean }
  | { kind: 'loaded'; page: AgentDisputePage };

@Component({
  selector: 'app-agent-disputes-page',
  imports: [DatePipe, DecimalMoney, ReactiveFormsModule, RouterLink],
  templateUrl: './agent-disputes-page.html',
  styleUrl: './agent-disputes-page.css',
})
export class AgentDisputesPage implements OnInit {
  private readonly api = inject(AgentDisputesApi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly requests = new Subject<{ filters: AgentDisputeFilters; cursor: string | null }>();
  @ViewChild('queueHeading') private queueHeading?: ElementRef<HTMLHeadingElement>;
  protected readonly state = signal<QueueState>({ kind: 'loading' });
  protected readonly filters = new FormGroup({
    search: new FormControl('', { nonNullable: true }),
    category: new FormControl<AgentDisputeCategory | ''>('', { nonNullable: true }),
    caseStatus: new FormControl<AgentCaseStatus | ''>('', { nonNullable: true }),
  });
  protected readonly pageNumber = signal(1);
  private cursors: (string | null)[] = [null];
  private focusAfterPageChange = false;
  private activeFilters: AgentDisputeFilters = this.filters.getRawValue();

  ngOnInit(): void {
    this.requests.pipe(
      switchMap(({ filters, cursor }) => this.api.list(filters, cursor).pipe(
        map((page): QueueState => ({ kind: 'loaded', page })),
        catchError((error: unknown) => of(this.errorState(error))),
        startWith<QueueState>({ kind: 'loading' }),
      )),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe((state) => {
      this.state.set(state);
      if (this.focusAfterPageChange && state.kind !== 'loading') {
        this.focusAfterPageChange = false;
        this.queueHeading?.nativeElement.focus();
      }
    });
    this.load();
  }

  protected applyFilters(): void {
    this.activeFilters = this.filters.getRawValue();
    this.cursors = [null];
    this.pageNumber.set(1);
    this.focusAfterPageChange = false;
    this.load();
  }

  protected nextPage(): void {
    const state = this.state();
    if (state.kind !== 'loaded' || !state.page.pageInfo.hasNextPage
      || state.page.pageInfo.nextCursor === undefined) return;
    this.cursors = this.cursors.slice(0, this.pageNumber());
    this.cursors.push(state.page.pageInfo.nextCursor);
    this.pageNumber.update(page => page + 1);
    this.focusAfterPageChange = true;
    this.load();
  }

  protected previousPage(): void {
    if (this.pageNumber() <= 1) return;
    this.pageNumber.update(page => page - 1);
    this.focusAfterPageChange = true;
    this.load();
  }

  protected load(): void {
    this.requests.next({ filters: this.activeFilters, cursor: this.cursors[this.pageNumber() - 1] ?? null });
  }

  protected readonly decimalFromMinorUnits = decimalFromMinorUnits;

  protected categoryLabel(category: AgentDisputeCategory): string {
    return category === 'CardPurchase' ? 'Card purchase'
      : category === 'DebitOrder' ? 'Debit order' : 'Bank charge';
  }

  private errorState(error: unknown): QueueState {
    if (!(error instanceof HttpErrorResponse)) {
      return { kind: 'error', message: 'Unable to load the Agent queue. Please try again.', retryable: false };
    }
    const retryable = error.status === 0 || error.status === 429 || error.status >= 500;
    return {
      kind: 'error',
      message: retryable
        ? 'Unable to load the Agent queue. Please try again.'
        : 'Unable to access the Agent queue. Check your bank employee session.',
      retryable,
    };
  }
}
