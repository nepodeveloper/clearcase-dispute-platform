import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, ValidatorFn } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { firstValueFrom, Subject, catchError, map, of, startWith, switchMap } from 'rxjs';
import { DecimalMoney } from '../transactions/decimal-money';
import {
  AgentDisputeDetails,
  AgentDisputesApi,
  decimalFromMinorUnits,
} from './agent-disputes-api';

type DetailState =
  | { kind: 'loading' }
  | { kind: 'error'; message: string; retryable: boolean }
  | { kind: 'loaded'; dispute: AgentDisputeDetails };

function normalizedLengthValidator(minimum: number, maximum: number): ValidatorFn {
  return control => {
    const text = typeof control.value === 'string' ? control.value.trim().normalize('NFC') : '';
    return text.length < minimum || text.length > maximum ? { length: true } : null;
  };
}

@Component({
  selector: 'app-agent-dispute-details-page',
  imports: [DatePipe, DecimalMoney, ReactiveFormsModule, RouterLink],
  templateUrl: './agent-dispute-details-page.html',
  styleUrl: './agent-dispute-details-page.css',
})
export class AgentDisputeDetailsPage implements OnInit {
  private readonly api = inject(AgentDisputesApi);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  private readonly requests = new Subject<string>();
  private disputeId = '';
  private noteRequestKey: string | null = null;
  protected readonly state = signal<DetailState>({ kind: 'loading' });
  protected readonly notePending = signal(false);
  protected readonly noteError = signal('');
  protected readonly retryableNoteError = signal(false);
  protected readonly noteConfirmation = signal('');
  protected readonly noteForm = new FormGroup({
    text: new FormControl('', {
      nonNullable: true,
      validators: [normalizedLengthValidator(15, 2000)],
    }),
  });
  protected readonly decimalFromMinorUnits = decimalFromMinorUnits;

  constructor() {
    this.noteForm.controls.text.valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        if (!this.notePending()) this.noteRequestKey = null;
        this.noteError.set('');
        this.retryableNoteError.set(false);
        this.noteConfirmation.set('');
      });

    this.requests.pipe(
      switchMap(id => this.api.get(id).pipe(
        map((dispute): DetailState => ({ kind: 'loaded', dispute })),
        catchError((error: unknown) => of(this.errorState(error))),
        startWith<DetailState>({ kind: 'loading' }),
      )),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe(state => this.state.set(state));
  }

  ngOnInit(): void {
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(parameters => {
      this.disputeId = parameters.get('id') ?? '';
      this.noteForm.reset();
      this.noteRequestKey = null;
      this.noteError.set('');
      this.retryableNoteError.set(false);
      this.noteConfirmation.set('');
      this.load();
    });
  }

  protected load(): void {
    this.requests.next(this.disputeId);
  }

  protected async saveNote(): Promise<void> {
    if (this.notePending()) return;
    if (this.noteForm.invalid) {
      this.noteForm.controls.text.markAsTouched();
      this.noteError.set('Enter an internal note of 15 to 2000 normalized characters.');
      return;
    }

    const text = this.noteForm.controls.text.value.trim().normalize('NFC');
    this.noteRequestKey ??= crypto.randomUUID();
    this.notePending.set(true);
    this.noteError.set('');
    this.retryableNoteError.set(false);
    this.noteConfirmation.set('');
    try {
      await firstValueFrom(this.api.createNote(this.disputeId, text, this.noteRequestKey));
      this.noteRequestKey = null;
      this.noteForm.reset();
      this.noteConfirmation.set('Private investigation note saved.');
      this.load();
    } catch (error: unknown) {
      this.noteError.set(this.noteErrorMessage(error));
      this.retryableNoteError.set(error instanceof HttpErrorResponse
        && (error.status === 0 || error.status === 429 || error.status >= 500));
    } finally {
      this.notePending.set(false);
    }
  }

  protected categoryLabel(category: string): string {
    return category === 'CardPurchase' ? 'Card purchase'
      : category === 'DebitOrder' ? 'Debit order' : 'Bank charge';
  }

  private errorState(error: unknown): DetailState {
    if (error instanceof HttpErrorResponse && error.status === 404) {
      return { kind: 'error', message: 'This dispute could not be found in the operational scope.', retryable: false };
    }
    const retryable = error instanceof HttpErrorResponse
      && (error.status === 0 || error.status === 429 || error.status >= 500);
    return {
      kind: 'error',
      message: retryable
        ? 'Unable to load the investigation. Please try again.'
        : 'Unable to access the investigation. Check your bank employee session.',
      retryable,
    };
  }

  private noteErrorMessage(error: unknown): string {
    if (!(error instanceof HttpErrorResponse)) return 'Unable to save the note. Retry with the same text.';
    if (error.status === 409) return 'This note request conflicts with an earlier request. Refresh the case before retrying.';
    if (error.status === 401 || error.status === 403) return 'Your bank employee session cannot create this note.';
    if (error.status === 404) return 'This dispute is no longer available in the operational scope.';
    if (error.status === 400) return 'Check the note text and request security token, then try again.';
    return 'Unable to save the note. Retry with the same text.';
  }
}
